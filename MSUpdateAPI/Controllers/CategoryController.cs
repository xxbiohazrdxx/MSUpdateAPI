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
		public async Task<ActionResult> Get(CancellationToken Token, [FromQuery] bool ShowDisabled = false)
		{
			if (!await service.IsInitialSyncCompleted(Token))
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Initial metadata seeding is still in progress. Try again later.");
			}

			if (ShowDisabled)
			{
				return Ok(await service.GetAllCategories(Token));
			}

			return Ok(await service.GetCategories(Token));
		}
	}
}
