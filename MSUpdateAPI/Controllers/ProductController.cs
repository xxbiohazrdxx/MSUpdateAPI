using Microsoft.AspNetCore.Mvc;
using MSUpdateAPI.Services;
using System.ServiceModel.Channels;

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
			if (ShowDisabled)
			{
				var allProducts = await service.GetAllProducts();
				return allProducts == null ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Ok(allProducts);
			}

			var products = await service.GetProducts();
			return products == null ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Ok(products);
		}
	}
}
