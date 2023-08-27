namespace MSUpdateAPI.Models
{
	// Detectoids are not actually used, but by keeping track of the ones that have already been processed we can exclude them from future
	// metadata downloads, speeding up the syncronization process
	public class Detectoid
	{
		public Guid Id { get; set; }
		public int Revision { get; set; }
		public string Name { get; set; }

		public Detectoid() { }
	}
}
