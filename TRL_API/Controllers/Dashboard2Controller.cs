using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using TRL_API.Data;
using TRL_API.Models;

namespace TRL_API.Controllers
{
    public class Dashboard2Controller : Controller
    {
        private readonly AppDbContext _context;

        public Dashboard2Controller(AppDbContext context)
        {
            _context = context;
        }

        // GET: Dashboard
        public async Task<IActionResult> Index()
        {
            return View(await _context.Dashboard.ToListAsync());
        }

        [HttpGet("monthly")]
        public async Task<IActionResult> Monthly(int months = 7, int? buildingId = null)
        {
            var now = DateTime.Now;
            var startMonth = new DateTime(now.Year, now.Month, 1).AddMonths(-months + 1);
            var result = new List<Dashboard>();

            for (int i = 0; i < months; i++)
            {
                var mStart = startMonth.AddMonths(i);
                var mEnd = mStart.AddMonths(1).AddTicks(-1);

                // Tenants that existed up to this month
                var tenantQuery = _context.Tenants.AsQueryable();

                if (buildingId.HasValue)
                    tenantQuery = tenantQuery.Where(t =>
                        t.Unit != null &&
                        t.Unit.BuildingId == buildingId.Value);

                tenantQuery = tenantQuery.Where(t => t.CreatedAt <= mEnd);

                var tenantCount = await tenantQuery.CountAsync();
                var totalRent = await tenantQuery.SumAsync(t => (decimal?)t.MonthlyRent) ?? 0m;

                // Payments received during this month
                var paymentQuery = _context.Payments
                    .Where(p => p.PaymentDate >= mStart && p.PaymentDate <= mEnd);

                if (buildingId.HasValue)
                    paymentQuery = paymentQuery.Where(p =>
                        p.Tenant != null &&
                        p.Tenant.Unit != null &&
                        p.Tenant.Unit.BuildingId == buildingId.Value);

                var collected = await paymentQuery.SumAsync(p => (decimal?)p.Amount) ?? 0m;

                result.Add(new Dashboard
                {
                    Month = mStart.ToString("yyyy-MM"),
                    TenantCount = tenantCount,
                    TotalRent = totalRent,
                    Collected = collected
                });
            }

            return Ok(result);
        }

        // GET: Dashboard/Details/5
        public async Task<IActionResult> Details(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var dashboard = await _context.Dashboard
                .FirstOrDefaultAsync(m => m.Month == id);
            if (dashboard == null)
            {
                return NotFound();
            }

            return View(dashboard);
        }

        // GET: Dashboard/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Dashboard/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Month,TenantCount,TotalRent,Collected")] Dashboard dashboard)
        {
            if (ModelState.IsValid)
            {
                _context.Add(dashboard);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(dashboard);
        }

        // GET: Dashboard/Edit/5
        public async Task<IActionResult> Edit(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var dashboard = await _context.Dashboard.FindAsync(id);
            if (dashboard == null)
            {
                return NotFound();
            }
            return View(dashboard);
        }

        // POST: Dashboard/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, [Bind("Month,TenantCount,TotalRent,Collected")] Dashboard dashboard)
        {
            if (id != dashboard.Month)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(dashboard);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DashboardExists(dashboard.Month))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(dashboard);
        }

        // GET: Dashboard/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var dashboard = await _context.Dashboard
                .FirstOrDefaultAsync(m => m.Month == id);
            if (dashboard == null)
            {
                return NotFound();
            }

            return View(dashboard);
        }

        // POST: Dashboard/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var dashboard = await _context.Dashboard.FindAsync(id);
            if (dashboard != null)
            {
                _context.Dashboard.Remove(dashboard);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool DashboardExists(string id)
        {
            return _context.Dashboard.Any(e => e.Month == id);
        }
    }
}
