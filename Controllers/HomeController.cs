using FruitBilling.Web.Data;
using FruitBilling.Web.Models; // 👈 این خط بسیار مهم است
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FruitBilling.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        public HomeController(AppDbContext db) { _db = db; }

        // صفحه اصلی (کارت‌های منو)
        public async Task<IActionResult> Index()
        {
            return View();
        }

     


        // ۱. نمای سه‌بعدی ساده (خاکستری، بدون تصویر ماهواره‌ای)
        [HttpGet]
        public IActionResult Map3DViewer(int? farmId = null)
        {
            ViewBag.FarmId = farmId;
            return View();
        }

        // ۲. نمای سه‌بعدی پیشرفته (با تصاویر ماهواره‌ای، ساخت مزارع و قطعات)
        [HttpGet]
        public IActionResult Map3D(int? farmId = null)
        {
            ViewBag.FarmId = farmId;
            return View();
        }
    }
}