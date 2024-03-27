﻿using Microsoft.AspNetCore.Mvc;

namespace Finance.Expensia.Web.Areas.Core.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Edit(string name, string column, string value)
        {
            return Json(new { success = true });
        }
    }
}
