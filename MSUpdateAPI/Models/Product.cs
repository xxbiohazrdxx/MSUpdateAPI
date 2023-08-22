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
		public string Name { get; set; }
		public List<Product> Subproducts { get; set; } = new List<Product>();
		[JsonIgnore]
		public List<string> Categories { get; set; } = new List<string>();
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

		public Product(Guid Id, string Name, IEnumerable<Product>? Subproducts)
		{
			this.Id = Id;
			this.Name = Name;
			this.Subproducts.AddRange(Subproducts ?? Array.Empty<Product>());
		}
	}
}
