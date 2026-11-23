namespace FruitBilling.Web.Models;

public class Tree
{
    public int Id { get; set; }
    public int TreeRowId { get; set; }
    public TreeRow? TreeRow { get; set; }

    public int Number { get; set; }                       // شماره درخت در ردیف
    public string ProductType { get; set; } = "";         // نوع
    public string? Name { get; set; }                     // نام/برچسب

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string? HealthStatus { get; set; }             // سالم، بیمار، خشک
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}