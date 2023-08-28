using Microsoft.AspNetCore.Mvc;
using MSUpdateAPI.Services;

namespace MSUpdateAPI.Controllers
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
			var update = await service.GetUpdate(Id);

			return update == null ? NotFound("An update with the provided id was not found.") : Ok(update);
		}

		[HttpGet]
		[Route("/api/update/{Id}/superseding")]
		public async Task<ActionResult> GetSuperseding(Guid Id)
		{
			var update = await service.GetSupersedingUpdate(Id);

			return update == null ? NotFound("A superseding update with the provided id was not found.") : Ok(update);
		}

		[HttpGet]
		public async Task<ActionResult> Get([FromQuery]Guid? Category = null, [FromQuery]Guid? Product = null, 
			[FromQuery] string? SearchString = null)
		{
			return Ok(await service.GetUpdates(Category, Product, SearchString));
		}
	}
}
