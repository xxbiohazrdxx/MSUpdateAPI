using System.Text.Json.Serialization;

namespace UpdateLib.Models
{
	// This additional OwnedProduct class is needed as it is an owned type of the Update entity. Product cannot be used since it is 
	// already mapped to a container. 
	public class OwnedProduct
	{
		public Guid Id { get; set; } = default!;
		public string Name { get; set; } = default!;

		public OwnedProduct() { }
	}

	public class Product
	{
		public Guid Id { get; set; } = default!;
		[JsonIgnore]
		public int Revision { get; set; } = default!;
		public string Name { get; set; } = default!;
		public List<Product> Subproducts { get; set; } = new List<Product>();
		[JsonIgnore]
		public List<string> Categories { get; set; } = new List<string>();
		[JsonIgnore]
		public bool Enabled
		{
			get
			{
				// If there are no subproducts, then this is the leaf in our Product tree, just return the value of Enabled
				if (Subproducts.Count == 0)
				{
					return enabled;
				}

				// If there are subproducts, then this is not a leaf. Return true if this product or any subproducts are enabled
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
