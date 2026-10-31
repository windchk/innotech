namespace Innotech.Sap.Api.Models
{
    public class JSON_Response
    {

        public class ORDR_Response
        {
            public string SO_DocNum { get; set; }
            public string Status { get; set; }
            public int errCode { get; set; }
            public string errMsg { get; set; }
        }
    }
}
