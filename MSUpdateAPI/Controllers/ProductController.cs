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
				var message = string.Format("Metadata is being refreshed: {0}", service.LastLogMessage);
				return StatusCode(StatusCodes.Status503ServiceUnavailable, message);
			}

			if (ShowDisabled)
			{
				return Ok(await service.GetAllProducts());
			}

			return Ok(await service.GetProducts());
		}
	}
}
