using System.ComponentModel.DataAnnotations;

namespace Innotech.Sap.Api.Models
{
    public class ORDR
    {
        public class Sales_Order
        {
            public int DocEntry { get; set; }
            public string U_OLDCODE { get; set; }
            [StringLength(20)]
            public string U_SO_Ref { get; set; }
            public short? U_So_Line_Ref { get; set; }
        }
    }
}
