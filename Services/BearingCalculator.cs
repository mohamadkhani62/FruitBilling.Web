namespace FruitBilling.Web.Services
{
    public static class BearingCalculator
    {
        /// <summary>
        /// زاویه (درجه) از نقطه A به نقطه B.
        /// 0 = شمال، 90 = شرق، 180 = جنوب، 270 = غرب
        /// </summary>
        public static double CalculateBearing(
            double lat1, double lng1,
            double lat2, double lng2)
        {
            double lat1Rad = lat1 * Math.PI / 180.0;
            double lat2Rad = lat2 * Math.PI / 180.0;
            double dLngRad = (lng2 - lng1) * Math.PI / 180.0;

            double y = Math.Sin(dLngRad) * Math.Cos(lat2Rad);
            double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                       Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLngRad);

            double bearing = Math.Atan2(y, x) * 180.0 / Math.PI;
            return (bearing + 360) % 360;
        }

        /// <summary>
        /// میانگین دو زاویه (برای راستای ردیف از دو نقطه).
        /// </summary>
        public static double AverageBearing(double b1, double b2)
        {
            double b1Rad = b1 * Math.PI / 180.0;
            double b2Rad = b2 * Math.PI / 180.0;
            double x = Math.Cos(b1Rad) + Math.Cos(b2Rad);
            double y = Math.Sin(b1Rad) + Math.Sin(b2Rad);
            double avg = Math.Atan2(y, x) * 180.0 / Math.PI;
            return (avg + 360) % 360;
        }
    }
}