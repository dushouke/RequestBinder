using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Web;
using System.ComponentModel;
using System.Collections;
using System.Collections.Specialized;
using System.Linq.Expressions;

namespace Lab.Util
{
    public interface IRequestBinder
    {
        object Converter(RequestBinderContext binderContext);
    }

    public class RequestBinderContext
    {
        private static readonly CultureInfo _staticCulture = CultureInfo.InvariantCulture;
        private CultureInfo _instanceCulture;
        public CultureInfo Culture
        {
            get
            {
                if (_instanceCulture == null)
                {
                    _instanceCulture = _staticCulture;
                }
                return _instanceCulture;
            }
            set
            {
                _instanceCulture = value;
            }
        }
        public string ParameterName { get; set; }
        public Type ModelType { get; set; }
        public int ParameterIndex { get; set; }
        public string ParameterRawValue { get; set; }
        public object ParameterDefaultValue { get; set; }
        private HttpContextBase httpContextBase;
        public HttpContextBase HttpContextBase
        {
            get
            {
                if (httpContextBase == null)
                {
                    if (HttpContext.Current == null)
                        throw new ArgumentNullException("HttpContextBase", "HttpContext.Current is null");
                    return new HttpContextWrapper(HttpContext.Current);
                }
                return httpContextBase;
            }
            set
            {
                httpContextBase = value;
            }
        }


    }
    internal class RequestBinderHelpers
    {
        public static Type ExtractGenericInterface(Type queryType, Type interfaceType)
        {
            Func<Type, bool> matchesInterface = t => t.IsGenericType && t.GetGenericTypeDefinition() == interfaceType;
            return (matchesInterface(queryType)) ? queryType : queryType.GetInterfaces().FirstOrDefault(matchesInterface);
        }
        public static object GetTypeDefaultValue(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }
            return null;
        }
        /// <summary>
        /// Simple model = int, string, etc.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool IsSimpleType(Type type)
        {
            return TypeDescriptor.GetConverter(type).CanConvertFrom(typeof(string));
        }
        public static object ChangeType(Type type, object value)
        {
            TypeConverter tc = TypeDescriptor.GetConverter(type);
            return tc.ConvertFrom(value);
        }

        private static object ConvertSimpleType(CultureInfo culture, object value, Type destinationType)
        {
            if (value == null || destinationType.IsInstanceOfType(value))
            {
                return value;
            }

            // if this is a user-input value but the user didn't type anything, return no value
            string valueAsString = value as string;
            if (valueAsString != null && valueAsString.Trim().Length == 0)
            {
                return null;
            }

            TypeConverter converter = TypeDescriptor.GetConverter(destinationType);
            bool canConvertFrom = converter.CanConvertFrom(value.GetType());
            if (!canConvertFrom)
            {
                converter = TypeDescriptor.GetConverter(value.GetType());
            }
            if (!(canConvertFrom || converter.CanConvertTo(destinationType)))
            {
                throw new InvalidOperationException("converter Exception");
            }

            try
            {
                object convertedValue = (canConvertFrom) ?
                     converter.ConvertFrom(null /* context */, culture, value) :
                     converter.ConvertTo(null /* context */, culture, value, destinationType);
                return convertedValue;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
        }

        public static object ConvertTo(CultureInfo culture, object value, Type destinationType)
        {
            if (value == null || destinationType.IsInstanceOfType(value))
            {
                return value;
            }

            // array conversion results in four cases, as below
            Array valueAsArray = value as Array;
            if (destinationType.IsArray)
            {
                Type destinationElementType = destinationType.GetElementType();
                if (valueAsArray != null)
                {
                    // case 1: both destination + source type are arrays, so convert each element
                    IList converted = Array.CreateInstance(destinationElementType, valueAsArray.Length);
                    for (int i = 0; i < valueAsArray.Length; i++)
                    {
                        converted[i] = ConvertSimpleType(culture, valueAsArray.GetValue(i), destinationElementType);
                    }
                    return converted;
                }
                else
                {
                    // case 2: destination type is array but source is single element, so wrap element in array + convert
                    object element = ConvertSimpleType(culture, value, destinationElementType);
                    IList converted = Array.CreateInstance(destinationElementType, 1);
                    converted[0] = element;
                    return converted;
                }
            }
            else if (valueAsArray != null)
            {
                // case 3: destination type is single element but source is array, so extract first element + convert
                if (valueAsArray.Length > 0)
                {
                    value = valueAsArray.GetValue(0);
                    return ConvertSimpleType(culture, value, destinationType);
                }
                else
                {
                    // case 3(a): source is empty array, so can't perform conversion
                    return null;
                }
            }
            // case 4: both destination + source type are single elements, so convert
            return ConvertSimpleType(culture, value, destinationType);
        }
    }


    public static class RequestBinder
    {
        private static readonly Dictionary<string, RuntimeTypeHandle>  registeredConverters = new Dictionary<string, RuntimeTypeHandle>();
        private static readonly Dictionary<string, IRequestBinder>  instantiatedConverters = new Dictionary<string, IRequestBinder>();
        private static readonly Dictionary<string, object> defaultValues = new Dictionary<string, object>();

        static RequestBinder()
        {
            RegistConvert(typeof(Boolean), typeof(BooleanRequestBinder));
        }
        public static T UpdateModel<T>() where T : class
        {
            return UpdateModel<T>(string.Empty);
        }
        public static T UpdateModel<T>(string parameterName)
        {
            return (T)UpdateModel(new RequestBinderContext
            {
                ModelType = typeof(T),
                ParameterName = parameterName
            });
        }
        private static object UpdateModel(RequestBinderContext binderContext)
        {
            if (RequestBinder.ContainsConvert(binderContext.ModelType))
            {
                binderContext.ParameterDefaultValue = GetTypeDefaultValue(binderContext.ModelType);
                binderContext.ParameterRawValue = GetRequestValue(binderContext);
                return GetConverter(binderContext.ModelType).Converter(binderContext);
            }

            if (RequestBinderHelpers.IsSimpleType(binderContext.ModelType))
            {
                if (string.IsNullOrWhiteSpace(binderContext.ParameterName))
                    throw new ArgumentNullException("parameterName");
                return BindSimpleModel(binderContext);
            }
            var requestValues = GetRequestValues(binderContext);
            if (requestValues != null && requestValues.Length > 0)
            {
                return BindSimpleModel(binderContext);
            }
            return BindComplexModel(binderContext);
        }
        private static NameValueCollection GetCleanRequest(NameValueCollection original)
        {
            NameValueCollection result = new NameValueCollection();
            if (original != null && original.AllKeys != null && original.AllKeys.Length > 0)
            {
                original.AllKeys.ToList().ForEach(okey =>
                {
                    var cleanKey = okey.Split("$".ToArray(), StringSplitOptions.RemoveEmptyEntries).Last();
                    var values = original.GetValues(okey);
                    values.ToList().ForEach(v =>
                    {
                        result.Add(cleanKey, v);
                    });
                });
            }
            return result;
        }
        private static string[] GetRequestValues(RequestBinderContext binderContext)
        {
            string[] result = new string[] { };
            result = GetCleanRequest(binderContext.HttpContextBase.Request.Form).GetValues(binderContext.ParameterName);
            if (result == null || result.Length == 0)
            {
                result = GetCleanRequest(binderContext.HttpContextBase.Request.QueryString).GetValues(binderContext.ParameterName);
            }
            return result;
        }
        private static string GetRequestValue(RequestBinderContext binderContext)
        {
            var temp = GetRequestValues(binderContext);
            if (temp != null && temp.Length > 0)
            {
                return temp.ElementAtOrDefault(binderContext.ParameterIndex);
            }
            return null;
        }
        private static object GetTypeDefaultValue(Type type)
        {
            object defaultValue;
            if (!defaultValues.TryGetValue(type.FullName, out defaultValue))
            {
                return RequestBinderHelpers.GetTypeDefaultValue(type);
            }
            return defaultValue;
        }

        private static object BindSimpleModel(RequestBinderContext binderContext)
        {

            object requestRawValue = GetRequestValues(binderContext);
            if (binderContext.ModelType.IsInstanceOfType(requestRawValue))
            {
                return requestRawValue;
            }
            if (binderContext.ModelType != typeof(string))
            {
                if (binderContext.ModelType.IsArray)
                {
                    Type elementType = binderContext.ModelType.GetElementType();
                    if (!RequestBinderHelpers.IsSimpleType(elementType) && string.IsNullOrWhiteSpace(binderContext.ParameterName))
                    {
                        throw new ArgumentNullException("parameterName", "简单类型必须指定ParameterName");
                    }
                    Type listType = typeof(List<>).MakeGenericType(elementType);
                    IList collection = CreateModel(listType) as IList;

                    binderContext.ParameterDefaultValue = GetTypeDefaultValue(binderContext.ModelType);
                    var values = GetRequestValues(binderContext);
                    if (values == null || values.Length == 0)
                    {
                        return binderContext.ParameterDefaultValue;
                    }
                    Array.ForEach(values, v =>
                    {
                        binderContext.ParameterDefaultValue = GetTypeDefaultValue(elementType);
                        binderContext.ModelType = elementType;
                        var temp = UpdateModel(binderContext);
                        binderContext.ParameterIndex++;
                        collection.Add(temp);
                    });
                    binderContext.ParameterIndex = 0;//reset parameterIndex

                    Array array = Array.CreateInstance(elementType, collection.Count);
                    collection.CopyTo(array, 0);
                    return array;
                }
                Type enumerableType = RequestBinderHelpers.ExtractGenericInterface(binderContext.ModelType, typeof(IEnumerable<>));
                if (enumerableType != null)
                {
                    Type elementType = enumerableType.GetGenericArguments()[0];
                    if (!RequestBinderHelpers.IsSimpleType(elementType) && string.IsNullOrWhiteSpace(binderContext.ParameterName))
                    {
                        throw new ArgumentNullException("parameterName", "简单类型必须指定ParameterName");
                    }
                    Type collectionType = typeof(ICollection<>).MakeGenericType(elementType);
                    IList collection = CreateModel(collectionType) as IList;

                    binderContext.ParameterDefaultValue = GetTypeDefaultValue(binderContext.ModelType);
                    var values = GetRequestValues(binderContext);
                    if (values == null || values.Length == 0)
                    {
                        return binderContext.ParameterDefaultValue;
                    }
                    Array.ForEach(values, v =>
                    {
                        binderContext.ParameterDefaultValue = GetTypeDefaultValue(elementType);
                        binderContext.ModelType = elementType;
                        var temp = UpdateModel(binderContext);
                        binderContext.ParameterIndex++;
                        collection.Add(temp);
                    });
                    binderContext.ParameterIndex = 0;//reset parameterIndex
                    return collection;
                }
            }
            binderContext.ParameterDefaultValue = GetTypeDefaultValue(binderContext.ModelType);
            binderContext.ParameterRawValue = GetRequestValue(binderContext);
            return RequestBinderBase.GeneralBind(binderContext);
        }
        private static object BindComplexModel(RequestBinderContext binderContext)
        {

            if (binderContext.ModelType.IsArray)
            {
                return null;//TODO
            }
            // special-case IDictionary<,> and ICollection<>
            Type dictionaryType = RequestBinderHelpers.ExtractGenericInterface(binderContext.ModelType, typeof(IDictionary<,>));
            if (dictionaryType != null)
            {
                return null;//TODO
            }
            Type enumerableType = RequestBinderHelpers.ExtractGenericInterface(binderContext.ModelType, typeof(IEnumerable<>));
            if (enumerableType != null)
            {
                return null;//TODO
            }
            // otherwise, just update the properties on the complex type
            binderContext.ParameterDefaultValue = GetTypeDefaultValue(binderContext.ModelType);
            return BindComplexElementalModel(binderContext);

        }
        private static object BindComplexElementalModel(RequestBinderContext binderContext)
        {

            if (binderContext.ParameterName.Length > 0 &&
                binderContext.ParameterName != binderContext.ModelType.Name)//防止嵌套循环
            {
                return null;
            }
            var memberBindings = binderContext.ModelType.GetProperties()
                                     .Select(p => Expression.Bind(p, Expression.Call(null,
                                                                     typeof(RequestBinder).GetMethod("UpdateModel", new[] { typeof(string) })
                                                                                          .MakeGenericMethod(p.PropertyType),
                                                                     Expression.Constant(binderContext.ParameterName.Length > 0 ? binderContext.ParameterName + "." + p.Name : p.Name))));
            var body = Expression.MemberInit(Expression.New(binderContext.ModelType), memberBindings);
            var func = Expression.Lambda<Func<object>>(body, new ParameterExpression[0]);
            return (func.Compile())();
        }
        private static object CreateModel(Type modelType)
        {
            Type typeToCreate = modelType;

            // we can understand some collection interfaces, e.g. IList<>, IDictionary<,>
            if (modelType.IsGenericType)
            {
                Type genericTypeDefinition = modelType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(IDictionary<,>))
                {
                    typeToCreate = typeof(Dictionary<,>).MakeGenericType(modelType.GetGenericArguments());
                }
                else if (genericTypeDefinition == typeof(IEnumerable<>) || genericTypeDefinition == typeof(ICollection<>) || genericTypeDefinition == typeof(IList<>))
                {
                    typeToCreate = typeof(List<>).MakeGenericType(modelType.GetGenericArguments());
                }
            }

            // fallback to the type's default constructor
            return Activator.CreateInstance(typeToCreate);
        }
        /// <summary>
        /// 注册类型默认值
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="defaultValue"></param>
        public static void RegistDefaultValue<T>(T defaultValue)
        {
            string typeFullName = typeof(T).FullName;
            if (defaultValues.ContainsKey(typeFullName))
            {
                defaultValues.Remove(typeFullName);
            }
            defaultValues.Add(typeFullName, defaultValue);
        }
        /// <summary>
        /// 注册类型转换器
        /// </summary>
        /// <param name="binderForType"></param>
        /// <param name="requestBinderType"></param>
        public static void RegistConvert(Type binderForType, Type requestBinderType)
        {
            Type interfaceType = requestBinderType.GetInterface("IRequestBinder");
            if (interfaceType != null)
            {
                var fullName = binderForType.FullName;
                if (registeredConverters.ContainsKey(fullName))
                    registeredConverters.Remove(fullName);
                registeredConverters.Add(fullName, requestBinderType.TypeHandle);
            }
        }

        private static bool ContainsConvert(Type type)
        {
            return registeredConverters.ContainsKey(type.FullName);
        }

        private static IRequestBinder GetConverter(Type type)
        {
            var fullName = type.FullName;
            if (!registeredConverters.ContainsKey(fullName))
                throw new Exception(
                  "No RequestBinder found for Type: " + fullName);
            if (instantiatedConverters.ContainsKey(fullName))
                return instantiatedConverters[fullName];
            else
            {
                var typeHandle = registeredConverters[fullName];
                IRequestBinder converter =
                  (IRequestBinder)Activator.CreateInstance(
                  Type.GetTypeFromHandle(typeHandle));
                instantiatedConverters.Add(fullName, converter);
                return converter;
            }
        }
    }

    public abstract class RequestBinderBase : IRequestBinder
    {
        public static object GeneralBind(RequestBinderContext binderContext)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(binderContext.ParameterRawValue))
                {
                    return binderContext.ParameterDefaultValue;
                }
                return RequestBinderHelpers.ChangeType(binderContext.ModelType, binderContext.ParameterRawValue);
            }
            catch
            {
                return binderContext.ParameterDefaultValue;
            }
        }
        #region IRequestBinder Members
        public abstract object Converter(RequestBinderContext binderContext);
        #endregion
    }



    internal class BooleanRequestBinder : RequestBinderBase
    {
        public override object Converter(RequestBinderContext binderContext)
        {
            if (!String.IsNullOrEmpty(binderContext.ParameterRawValue))
            {
                switch (binderContext.ParameterRawValue.Trim())
                {
                    case "False":
                    case "false":
                    case "0":
                    case "off":
                    case "":
                        return false;
                    case "True":
                    case "true":
                    case "1":
                    case "on":
                        return true;
                    default:
                        return false;
                }
            }
            else
                return binderContext.ParameterDefaultValue;
        }
    }

}
