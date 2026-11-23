using FruitBilling.Web.Models;

namespace FruitBilling.Web.Services
{
    public class TreeLayoutCalculator
    {
        /// <summary>
        /// محاسبه موقعیت همه درختان یک ردیف.
        /// </summary>
        /// <param name="firstTreeLat">عرض جغرافیایی درخت اول</param>
        /// <param name="firstTreeLng">طول جغرافیایی درخت اول</param>
        /// <param name="treeCount">تعداد درختان</param>
        /// <param name="spacingMeters">فاصله بین درختان (متر)</param>
        /// <param name="bearingDegrees">زاویه ردیف (0 = شمال، 90 = شرق)</param>
        /// <returns>لیست مختصات درختان</returns>
        public static List<(double lat, double lng)> CalculateTreePositions(
            double firstTreeLat, double firstTreeLng,
            int treeCount, double spacingMeters, double bearingDegrees)
        {
            var result = new List<(double, double)>();
            result.Add((firstTreeLat, firstTreeLng));

            double bearingRad = bearingDegrees * Math.PI / 180.0;
            double earthRadius = 6371000.0; // متر

            for (int i = 1; i < treeCount; i++)
            {
                double distance = spacingMeters * i;
                double angularDistance = distance / earthRadius;

                double lat1 = firstTreeLat * Math.PI / 180.0;
                double lng1 = firstTreeLng * Math.PI / 180.0;

                double lat2 = Math.Asin(
                    Math.Sin(lat1) * Math.Cos(angularDistance) +
                    Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearingRad)
                );

                double lng2 = lng1 + Math.Atan2(
                    Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(lat1),
                    Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2)
                );

                result.Add((lat2 * 180.0 / Math.PI, lng2 * 180.0 / Math.PI));
            }

            return result;
        }

        /// <summary>
        /// محاسبه چند ردیف موازی (فاصله عرضی).
        /// </summary>
        public static List<List<(double lat, double lng)>> CalculateRows(
            double firstTreeLat, double firstTreeLng,
            int treeCountPerRow, int rowCount,
            double spacingAlongMeters, double spacingAcrossMeters,
            double bearingDegrees)
        {
            var rows = new List<List<(double, double)>>();

            for (int r = 0; r < rowCount; r++)
            {
                // نقطه شروع هر ردیف: درخت اول + جابجایی عرضی
                double acrossBearing = (bearingDegrees + 90) % 360;
                var startPoint = OffsetPoint(firstTreeLat, firstTreeLng,
                    spacingAcrossMeters * r, acrossBearing);

                var row = CalculateTreePositions(
                    startPoint.lat, startPoint.lng,
                    treeCountPerRow, spacingAlongMeters, bearingDegrees);

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// جابجایی یک نقطه در جهت و فاصله مشخص.
        /// </summary>
        public static (double lat, double lng) OffsetPoint(
            double lat, double lng, double distanceMeters, double bearingDegrees)
        {
            double bearingRad = bearingDegrees * Math.PI / 180.0;
            double earthRadius = 6371000.0;
            double angularDistance = distanceMeters / earthRadius;

            double lat1 = lat * Math.PI / 180.0;
            double lng1 = lng * Math.PI / 180.0;

            double lat2 = Math.Asin(
                Math.Sin(lat1) * Math.Cos(angularDistance) +
                Math.Cos(lat1) * Math.Sin(angularDistance) * Math.Cos(bearingRad)
            );

            double lng2 = lng1 + Math.Atan2(
                Math.Sin(bearingRad) * Math.Sin(angularDistance) * Math.Cos(lat1),
                Math.Cos(angularDistance) - Math.Sin(lat1) * Math.Sin(lat2)
            );

            return (lat2 * 180.0 / Math.PI, lng2 * 180.0 / Math.PI);
        }
    }
}