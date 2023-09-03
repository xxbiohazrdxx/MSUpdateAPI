namespace UpdateLib.Models
{
	public class File
	{
		public string FileName { get; set; } = default!;
		public string Source { get; set; } = default!;
		public DateTime ModifiedDate { get; set; } = default!;
		public FileDigest Digest { get; set; } = default!;
		public ulong Size { get; set; } = default!;

		public File() { }
	}
}
