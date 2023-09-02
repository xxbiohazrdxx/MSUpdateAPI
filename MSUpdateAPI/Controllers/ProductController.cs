using Microsoft.AspNetCore.Mvc;
using UpdateAPI.Services;

namespace UpdateAPI.Controllers
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
		public async Task<ActionResult> Get(CancellationToken Token, [FromQuery] bool ShowDisabled = false)
		{
			if (!await service.IsInitialSyncCompleted(Token))
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Initial metadata seeding is still in progress. Try again later.");
			}

			if (ShowDisabled)
			{
				var allProducts = await service.GetAllProducts(Token);
				return allProducts == null ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Ok(allProducts);
			}

			var products = await service.GetProducts(Token);
			return products == null ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Ok(products);
		}
	}
}
