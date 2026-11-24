namespace FruitBilling.Web.Models
{
    public class PermitReconciliationViewModel
    {
        public int? HarvestCycleId { get; set; }
        public string? HarvestCycleName { get; set; }
        public List<PermitReconciliationRow> Rows { get; set; } = new();
        public int MatchedCount => Rows.Count(x => x.Status == "تطبیق کامل");
        public int MismatchCount => Rows.Count(x => x.Status == "مغایرت اطلاعات");
        public int MissingCount => Rows.Count(x => x.Status == "فاقد طرف مقابل");
        public int AmbiguousCount => Rows.Count(x => x.Status == "تطبیق مبهم");
        public int IncompleteCount => Rows.Count(x => x.Status == "اطلاعات ناکافی");
    }

    public class PermitReconciliationRow
    {
        public string Status { get; set; } = "";
        public string Details { get; set; } = "";
        public string TicketNo { get; set; } = "";
        public string Date { get; set; } = "";
        public string VehiclePlate { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string VarietyName { get; set; } = "";
        public string FarmPermitNumber { get; set; } = "";
        public string SortingPermitNumber { get; set; } = "";
        public int? FarmBasketCount { get; set; }
        public int? SortingBasketCount { get; set; }
        public int? FarmEmptyWeight { get; set; }
        public int? SortingEmptyWeight { get; set; }
        public int? FarmGrossWeight { get; set; }
        public int? SortingGrossWeight { get; set; }
    }

    public class ReconciliationTicketSnapshot
    {
        public string Side { get; set; } = "";
        public string Key { get; set; } = "";
        public string TicketNo { get; set; } = "";
        public string Date { get; set; } = "";
        public string VehiclePlate { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string VarietyName { get; set; } = "";
        public string PermitNumber { get; set; } = "";
        public int? BasketCount { get; set; }
        public int EmptyWeight { get; set; }
        public int GrossWeight { get; set; }
    }
}
