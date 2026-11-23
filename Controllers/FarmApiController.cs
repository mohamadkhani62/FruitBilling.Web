using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using FruitBilling.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FruitBilling.Web.Controllers
{
    // ❗ مسیر صریح تا token [controller] به FarmApi تبدیل نشود
    [Route("api/farm")]
    [ApiController]
    public class FarmApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        public FarmApiController(AppDbContext db) { _db = db; }

        // ---------- مزرعه ----------
        [HttpGet]                            // GET  /api/farm
        public async Task<IActionResult> GetAll()
        {
            var farms = await _db.Farms
                .Include(f => f.Plots)
                .Select(f => new
                {
                    f.Id,
                    f.Name,
                    f.CoordinatesJson,
                    f.AreaHectare,
                    Plots = f.Plots.Select(p => new { p.Id, p.Name, p.CoordinatesJson })
                })
                .ToListAsync();
            return Ok(farms);
        }

        [HttpPost]                           // POST /api/farm
        public async Task<IActionResult> CreateFarm([FromBody] Farm farm)
        {
            farm.Id = 0;
            farm.CreatedAt = DateTime.Now;
            _db.Farms.Add(farm);
            await _db.SaveChangesAsync();
            return Ok(new { farm.Id });
        }

        // ---------- قطعه ----------
        [HttpPost("plot")]                   // POST /api/farm/plot
        public async Task<IActionResult> CreatePlotApi([FromBody] Plot plot)
        {
            plot.Id = 0;
            plot.CreatedAt = DateTime.Now;
            _db.Plots.Add(plot);
            await _db.SaveChangesAsync();
            return Ok(new { plot.Id });
        }

        [HttpGet("plot/{plotId}/rows")]      // GET  /api/farm/plot/5/rows
        public async Task<IActionResult> GetPlotRows(int plotId)
        {
            var rows = await _db.TreeRows
                .Where(r => r.PlotId == plotId)
                .Include(r => r.Trees)
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.SectionIndex,
                    r.ProductType,
                    r.SpacingAlongMeters,
                    r.SpacingAcrossMeters,
                    r.RowCount,
                    r.FirstTreeLat,
                    r.FirstTreeLng,
                    r.DirectionPoint1Lat,
                    r.DirectionPoint1Lng,
                    r.DirectionPoint2Lat,
                    r.DirectionPoint2Lng,
                    r.BearingDegrees,
                    r.TreeCount,
                    Trees = r.Trees.Select(t => new
                    {
                        t.Id,
                        t.Number,
                        t.ProductType,
                        t.Latitude,
                        t.Longitude,
                        t.HealthStatus,
                        t.Notes
                    })
                })
                .ToListAsync();
            return Ok(rows);
        }

        // ---------- ردیف درختان (محاسبه خودکار) ----------
        [HttpPost("row")]                    // POST /api/farm/row
        public async Task<IActionResult> CreateRow([FromBody] CreateRowRequest3P req)
        {
            double bearing1 = BearingCalculator.CalculateBearing(
                req.FirstTreeLat, req.FirstTreeLng,
                req.DirectionPoint1Lat, req.DirectionPoint1Lng);

            double bearing2 = BearingCalculator.CalculateBearing(
                req.FirstTreeLat, req.FirstTreeLng,
                req.DirectionPoint2Lat, req.DirectionPoint2Lng);

            double finalBearing = BearingCalculator.AverageBearing(bearing1, bearing2);

            var positions = TreeLayoutCalculator.CalculateRows(
                req.FirstTreeLat, req.FirstTreeLng,
                req.TreeCountPerRow, req.RowCount,
                req.SpacingAlongMeters, req.SpacingAcrossMeters,
                finalBearing);

            var createdRows = new List<TreeRow>();
            for (int r = 0; r < positions.Count; r++)
            {
                var row = new TreeRow
                {
                    PlotId = req.PlotId,
                    Name = $"{req.ProductType} - ردیف {r + 1}",
                    SectionIndex = req.SectionIndex,
                    ProductType = req.ProductType,
                    SpacingAlongMeters = req.SpacingAlongMeters,
                    SpacingAcrossMeters = req.SpacingAcrossMeters,
                    RowCount = req.RowCount,
                    FirstTreeLat = req.FirstTreeLat,
                    FirstTreeLng = req.FirstTreeLng,
                    DirectionPoint1Lat = req.DirectionPoint1Lat,
                    DirectionPoint1Lng = req.DirectionPoint1Lng,
                    DirectionPoint2Lat = req.DirectionPoint2Lat,
                    DirectionPoint2Lng = req.DirectionPoint2Lng,
                    BearingDegrees = finalBearing,
                    TreeCount = positions[r].Count
                };

                for (int i = 0; i < positions[r].Count; i++)
                {
                    row.Trees.Add(new Tree
                    {
                        Number = i + 1,
                        ProductType = req.ProductType,
                        Latitude = positions[r][i].lat,
                        Longitude = positions[r][i].lng,
                        HealthStatus = "سالم"
                    });
                }

                _db.TreeRows.Add(row);
                createdRows.Add(row);
            }

            await _db.SaveChangesAsync();
            return Ok(new
            {
                rowsCreated = createdRows.Count,
                treeCount = createdRows.Sum(r => r.Trees.Count),
                bearing = finalBearing
            });
        }

        // ---------- درختان ----------
        [HttpDelete("tree/{id}")]            // DELETE /api/farm/tree/10
        public async Task<IActionResult> DeleteTree(int id)
        {
            var t = await _db.Trees.FindAsync(id);
            if (t == null) return NotFound();
            _db.Trees.Remove(t);
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("trees/bulk")]           // DELETE /api/farm/trees/bulk
        public async Task<IActionResult> DeleteTreesBulk([FromBody] List<int> ids)
        {
            var trees = await _db.Trees.Where(t => ids.Contains(t.Id)).ToListAsync();
            _db.Trees.RemoveRange(trees);
            await _db.SaveChangesAsync();
            return Ok(new { deleted = trees.Count });
        }

        [HttpPut("trees/bulk")]              // PUT /api/farm/trees/bulk
        public async Task<IActionResult> UpdateTreesBulk([FromBody] UpdateTreesRequest req)
        {
            var trees = await _db.Trees.Where(t => req.Ids.Contains(t.Id)).ToListAsync();
            foreach (var t in trees)
            {
                if (req.ProductType != null) t.ProductType = req.ProductType;
                if (req.HealthStatus != null) t.HealthStatus = req.HealthStatus;
                if (req.Notes != null) t.Notes = req.Notes;
            }
            await _db.SaveChangesAsync();
            return Ok(new { updated = trees.Count });
        }

        // ---------- جاده ----------
        [HttpPost("road")]                   // POST /api/farm/road
        public async Task<IActionResult> CreateRoad([FromBody] Road road)
        {
            road.Id = 0;
            road.CreatedAt = DateTime.Now;
            _db.Roads.Add(road);
            await _db.SaveChangesAsync();
            return Ok(new { road.Id });
        }

        [HttpGet("roads")]                   // GET /api/farm/roads?farmId=1
        public async Task<IActionResult> GetRoads(int? farmId = null, int? plotId = null)
        {
            var q = _db.Roads.AsQueryable();
            if (farmId.HasValue) q = q.Where(r => r.FarmId == farmId);
            if (plotId.HasValue) q = q.Where(r => r.PlotId == plotId);
            return Ok(await q.ToListAsync());
        }
    }

    // ---------- DTOها ----------
    public class CreateRowRequest3P
    {
        public int PlotId { get; set; }
        public int SectionIndex { get; set; } = 1;
        public string ProductType { get; set; } = "";
        public double FirstTreeLat { get; set; }
        public double FirstTreeLng { get; set; }
        public double DirectionPoint1Lat { get; set; }
        public double DirectionPoint1Lng { get; set; }
        public double DirectionPoint2Lat { get; set; }
        public double DirectionPoint2Lng { get; set; }
        public int TreeCountPerRow { get; set; }
        public int RowCount { get; set; } = 1;
        public double SpacingAlongMeters { get; set; }
        public double SpacingAcrossMeters { get; set; }
    }

    public class UpdateTreesRequest
    {
        public List<int> Ids { get; set; } = new();
        public string? ProductType { get; set; }
        public string? HealthStatus { get; set; }
        public string? Notes { get; set; }
    }
}