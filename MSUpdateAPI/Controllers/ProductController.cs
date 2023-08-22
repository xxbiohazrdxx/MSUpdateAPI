using Microsoft.AspNetCore.Mvc;
using MSUpdateAPI.Services;

namespace MSUpdateAPI.Controllers
{
	[ApiController]
	[Route("/api/product")]
	public class ProductController : ControllerBase
	{
		private readonly UpdateService service;
		public ProductController(UpdateService Service)
		{
			service = Service;
		}

		[HttpGet]
		public async Task<ActionResult> Get([FromQuery] bool ShowDisabled = false)
		{
			if (!service.MetadataLoaded)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Metadata is being refreshed.");
			}

			if (ShowDisabled)
			{
				return Ok(await service.GetAllProducts());
			}

			return Ok(await service.GetProducts());
		}
	}
}
