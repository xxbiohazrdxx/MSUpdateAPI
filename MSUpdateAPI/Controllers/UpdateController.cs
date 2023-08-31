using Microsoft.AspNetCore.Mvc;
using UpdateAPI.Services;

namespace UpdateAPI.Controllers
{
	[ApiController]
	[Route("/api/update")]
	public class UpdateController : ControllerBase
	{
		private readonly UpdateService service;
		public UpdateController(UpdateService Service)
		{
			service = Service;
		}

		[HttpGet]
		[Route("/api/update/{Id}")]
		public async Task<ActionResult> Get(Guid Id)
		{
			if (!service.IsInitialSyncCompleted())
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Initial metadata seeding is still in progress. Try again later.");
			}

			var update = await service.GetUpdate(Id);

			return update == null ? NotFound("An update with the provided id was not found.") : Ok(update);
		}

		[HttpGet]
		[Route("/api/update/{Id}/superseding")]
		public async Task<ActionResult> GetSuperseding(Guid Id)
		{
			if (!service.IsInitialSyncCompleted())
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Initial metadata seeding is still in progress. Try again later.");

			}
			var update = await service.GetSupersedingUpdate(Id);

			return update == null ? NotFound("A superseding update with the provided id was not found.") : Ok(update);
		}

		[HttpGet]
		public async Task<ActionResult> Get([FromQuery]Guid? Category = null, [FromQuery]Guid? Product = null, 
			[FromQuery] string? Query = null)
		{
			if (!service.IsInitialSyncCompleted())
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Initial metadata seeding is still in progress. Try again later.");
			}

			return Ok(await service.GetUpdates(Category, Product, Query));
		}
	}
}
