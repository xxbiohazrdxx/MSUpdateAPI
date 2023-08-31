using Microsoft.AspNetCore.Mvc;
using UpdateAPI.Services;

namespace UpdateAPI.Controllers
{
	[ApiController]
	[Route("/api/category")]
	public class CategoryController : ControllerBase
	{
		private readonly UpdateService service;
		public CategoryController(UpdateService Service)
		{
			service = Service;
		}

		[HttpGet]
		public async Task<ActionResult> Get([FromQuery] bool ShowDisabled = false)
		{
			if (!service.IsInitialSyncCompleted())
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Initial metadata seeding is still in progress. Try again later.");
			}

			if (ShowDisabled)
			{
				return Ok(await service.GetAllCategories());
			}

			return Ok(await service.GetCategories());
		}
	}
}
