using System.ComponentModel.DataAnnotations;

namespace FruitBilling.Web.Models
{
    public class TreeRow
    {
        public int Id { get; set; }

        public int PlotId { get; set; }
        public Plot? Plot { get; set; }

        [Display(Name = "نام")]
        public string Name { get; set; } = "";

        public int SectionIndex { get; set; } = 1;

        [Display(Name = "نوع درخت")]
        public string ProductType { get; set; } = "";

        [Display(Name = "فاصله طولی (م)")]
        public double SpacingAlongMeters { get; set; }

        [Display(Name = "فاصله عرضی (م)")]
        public double SpacingAcrossMeters { get; set; }

        public int RowCount { get; set; } = 1;

        // 👇 سه نقطه برای تعیین راستا
        public double FirstTreeLat { get; set; }
        public double FirstTreeLng { get; set; }
        public double DirectionPoint1Lat { get; set; }
        public double DirectionPoint1Lng { get; set; }
        public double DirectionPoint2Lat { get; set; }
        public double DirectionPoint2Lng { get; set; }

        // زاویه محاسبه‌شده
        public double BearingDegrees { get; set; }

        public int TreeCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<Tree> Trees { get; set; } = new();
    }
}