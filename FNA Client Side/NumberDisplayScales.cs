// AS3: com.company.assembleegameclient.util.NumberDisplayScales
// Complete 250+ abbreviation scales in descending order (highest exponent first).
// exp values are GROUP EXPONENTS (multiples of 3): "462" = 10^462, "3" = 10^3
// Synced with server BigIntUtils.cs AbbrevScales.
namespace VortexClient.Core
{
    public static class NumberDisplayScales
    {
        /// <summary>
        /// Returns the complete SCALES array with all 250+ abbreviations in descending order.
        /// Each entry: (exp, suffix) where exp is the group exponent (multiple of 3).
        /// Example: ("3", "k") means 10^3 = kilo, ("6", "M") means 10^6 = mega
        /// </summary>
        public static (string exp, string suffix)[] GetStandardScales()
        {
            return new (string, string)[]
            {
                // Highest exponents first (descending order)
                ("462", "YZCePi"), ("459", "XZCePi"),
                ("456", "WZCePi"), ("453", "VZCePi"),
                ("450", "UZCePi"), ("447", "TZCePi"),
                ("444", "SZCePi"), ("441", "RZCePi"),
                ("438", "QZCePi"), ("435", "PZCePi"),
                ("432", "NZCePi"), ("429", "MZCePi"),
                ("426", "LZCePi"), ("423", "KZCePi"),
                ("420", "JZCePi"), ("417", "IZCePi"),
                ("414", "HZCePi"), ("411", "GZCePi"),
                ("408", "FZCePi"), ("405", "EZCePi"),
                ("402", "DZCePi"), ("399", "CZCePi"),
                ("396", "BZCePi"), ("393", "AZCePi"),
                ("390", "AAZCePi"),("387", "ZCePi"),
                ("384", "YCePi"), ("381", "WCePi"),
                ("378", "VCePi"), ("375", "QCePi"),
                ("372", "TCePi"), ("369", "UCePi"),
                ("366", "DCePi"), ("363", "XCePi"),
                ("360", "HCePi"), ("357", "CePi"),
                ("354", "DePi"), ("351", "Pi"),
                ("348", "CeNa"), ("345", "DeNa"),
                ("342", "Na"), ("339", "CeMc"),
                ("336", "DeMc"), ("333", "Mc"),
                ("330", "NiMi"), ("327", "OtMi"),
                ("324", "SiMi"), ("321", "SeMi"),
                ("318", "QiMi"), ("315", "QaMi"),
                ("312", "TrMi"), ("309", "DuMi"),
                ("306", "CeMi"), ("303", "NgMi"),
                ("300", "OgMi"), ("297", "SgMi"),
                ("294", "sgMi"), ("291", "QgMi"),
                ("288", "qgMi"), ("285", "TgMi"),
                ("282", "TVtMi"),("279", "VtMi"),
                ("276", "DeMi"), ("273", "NoMi"),
                ("270", "OcMi"), ("267", "SpMi"),
                ("264", "SxMi"), ("261", "QnMi"),
                ("258", "QdMi"), ("255", "TMi"),
                ("252", "DMi"), ("249", "Mi"),
                ("246", "Ni"), ("243", "Ot"),
                ("240", "Si"), ("237", "Se"),
                ("234", "Qi"), ("231", "Qa"),
                ("228", "Tr"), ("225", "Du"),
                ("222", "Ce"), ("219", "Ng"),
                ("216", "Og"), ("213", "Sg"),
                ("210", "Nosg"),("207", "Ocsg"),
                ("204", "Spsg"),("201", "Sxsg"),
                ("198", "Qnsg"),("195", "Qdsg"),
                ("192", "Tsg"),("189", "Dsg"),
                ("186", "Usg"),("183", "sg"),
                ("180", "NoQg"),("177", "OcQg"),
                ("174", "SpQg"),("171", "SxQg"),
                ("168", "QnQg"),("165", "QdQg"),
                ("162", "TQg"), ("159", "DQg"),
                ("156", "UQg"), ("153", "Qg"),
                ("150", "Noqg"),("147", "Ocqg"),
                ("144", "Spqg"),("141", "Sxqg"),
                ("138", "Qnqg"),("135", "Qdqg"),
                ("132", "Tqg"), ("129", "Dqg"),
                ("126", "Uqg"), ("123", "qg"),
                ("120", "NoTg"),("117", "OcTg"),
                ("114", "SpTg"),("111", "SxTg"),
                ("108", "QnTg"),("105", "QdTg"),
                ("102", "TTg"), ("99",  "DTg"),
                ("96",  "UTg"), ("93",  "Tg"),
                ("90",  "NoVt"),("87",  "OcVt"),
                ("84",  "SpVt"),("81",  "SxVt"),
                ("78",  "QnVt"),("75",  "QdVt"),
                ("72",  "TVt"), ("69",  "DVt"),
                ("66",  "UVt"), ("63",  "Vt"),
                ("60",  "NoDe"),("57",  "OcDe"),
                ("54",  "SpDe"),("51",  "SxDe"),
                ("48",  "QnDe"),("45",  "QdDe"),
                ("42",  "TDe"), ("39",  "DDe"),
                ("36",  "UDe"), ("33",  "De"),
                ("30",  "No"),  ("27",  "Oc"),
                ("24",  "Sp"),  ("21",  "Sx"),
                ("18",  "Qn"),  ("15",  "Qd"),
                ("12",  "T"),   ("9",   "B"),
                ("6",   "M"),   ("3",   "k"),
            };
        }
    }
}
