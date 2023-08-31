using Microsoft.AspNetCore.Mvc;
using UpdateAPI.Services;

namespace UpdateAPI.Controllers
{
	[ApiController]
	[Route("/api")]
	public class StatusController : ControllerBase
	{
		private readonly UpdateService service;
		public StatusController(UpdateService Service)
		{
			service = Service;
		}

		[HttpGet]
		public ActionResult Get()
		{
			return Ok(service.GetStatus());
		}
	}
}
