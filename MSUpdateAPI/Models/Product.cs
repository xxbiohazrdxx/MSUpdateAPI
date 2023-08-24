using System.Text.Json.Serialization;

namespace MSUpdateAPI.Models
{
	public class OwnedProduct
	{
		public Guid Id { get; set; }
		public string Name { get; set; }

		public OwnedProduct() { }
	}

	public class Product
	{
		public Guid Id { get; set; }
		[JsonIgnore]
		public int Revision { get; set; }
		public string Name { get; set; }
		public List<Product> Subproducts { get; set; } = new List<Product>();
		[JsonIgnore]
		public List<string> Categories { get; set; } = default!;
		internal bool Enabled
		{
			get
			{
				if (Subproducts.Count == 0)
				{
					return enabled;
				}

				return enabled || Subproducts.Any(x => x.Enabled);
			}
			set
			{
				enabled = value;
			}
		}
		private bool enabled;

		public Product() { }
	}
}
