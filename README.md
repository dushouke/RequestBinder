#Get Start
##Intro
Mapping HTTP request data directly into  custom .NET objects (including simple type collections)

##How to Use

the html like is


	SysNo<input name="ctl00$ContentPlaceHolder1$WebUserControl11$SysNo" type="text" id="ContentPlaceHolder1_WebUserControl11_SysNo" /><br />
	Name<input name="ctl00$ContentPlaceHolder1$WebUserControl11$Name" type="text" id="ContentPlaceHolder1_WebUserControl11_Name" /><br />
	Time<input name="ctl00$ContentPlaceHolder1$WebUserControl11$Birthday" type="text" id="ContentPlaceHolder1_WebUserControl11_Birthday" /><br />
	Good<input id="ContentPlaceHolder1_WebUserControl11_Good" type="checkbox" name="ctl00$ContentPlaceHolder1$WebUserControl11$Good" /><br />
	Address.Name<input id="Address.Name" name="Address.Name" type="text" /><br />
	Address.SysNo<input id="Address.SysNO" name="Address.SysNo" type="text" /><br />
	Test<input id="Text1" name="Test" type="text" /><br />
	Test<input id="Text2" name="Test" type="text" /><br />


###Simple Type


	int sysNo = RequestBinder.UpdateModel<int>("SysNo");
	string Name = RequestBinder.UpdateModel<string>("Name");
	DateTime birthday = RequestBinder.UpdateModel<DateTime>("Birthday");
	string[] test = RequestBinder.UpdateModel<string[]>("Test");


###Complex Type

the model

    public class User
    {
        public int SysNo { get; set; }
        public string Name { get; set; }
        public bool Good { get; set; }
        public DateTime Birthday { get; set; }
        public Address Address { get; set; }
        public int[] Test { get; set; }
        public List<Order> Orders { get; set; }//not support
    }
    public class Order
    {
        public string OrderID { get; set; }

    }
    public class Address
    {
        public User User { get; set; }
        public int SysNo { get; set; }
        public string Name { get; set; }
    }

    
    //map like this
    User u = RequestBinder.UpdateModel<User>();

