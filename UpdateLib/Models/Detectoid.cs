namespace UpdateLib.Models
{
	// Detectoids are not actually used, but by keeping track of the ones that have already been processed we can exclude them from future
	// metadata downloads, speeding up the syncronization process
	public class Detectoid
	{
		public Guid Id { get; set; } = default!;
		public int Revision { get; set; } = default!;
		public string Name { get; set; } = default!;

		public Detectoid() { }
	}
}
