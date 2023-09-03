namespace UpdateLib.Models
{
	public class FileDigest
	{
		public string Algorithm { get; set; } = default!;
		public string Value { get; set; } = default!;

		public FileDigest () { }
	}
}
