namespace FruitBilling.Web.Models;

public class Farm
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string CoordinatesJson { get; set; } = "[]";  // مرز مزرعه
    public decimal? AreaHectare { get; set; }            // مساحت
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<Plot> Plots { get; set; } = new();
}