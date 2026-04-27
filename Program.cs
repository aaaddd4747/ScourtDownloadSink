using System.Net;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections.Generic;
namespace ScourtDownloadSink
{

	public class fileRecord
	{
		public FileStream fs { get; init; }
		public ulong length { get; init; }
		public ulong length_remaining { get; set; }
		public string name {get;init;}

	}


	class Program
{
	private const int ListenPort = 49553;
	private const string HostEn = "com3d2-shop-dl1-us.s-court.me";
	private const string HostJp = "com3d2-shop-dl1.s-court.me";
	private const string Host = HostEn;

	static async Task Main()
	{
		Console.Title = "ScourtDownloadSink";

		var client = new HttpClient();
		client.DefaultRequestHeaders.Add("User-Agent", "COM3D2UP");
		client.Timeout = TimeSpan.FromMinutes(5);

		var listener = new HttpListener();
		listener.Prefixes.Add($"http://localhost:{ListenPort}/");
		try
		{
			listener.Start();
		}
		catch (HttpListenerException e)
		{
			Console.Error.WriteLine(e.Message);
			return;
		}

		Console.WriteLine($"Listening on port {ListenPort}");

		while (true)
		{
			var context = listener.GetContext();

			var downloadUri = GetDownloadUri(context.Request.Url);
			Console.WriteLine();
			Console.WriteLine($"Retrieving shop item data...");
			using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
			if (response.IsSuccessStatusCode)
			{
				if (response.Content.Headers.ContentDisposition?.FileName != null)
				{
					var fileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
					var fileSize = response.Content.Headers.ContentLength;
					var bufferSize = 81920;
					ulong totalBytesRead = 0;
					var buffer = new byte[bufferSize];
					var files = new List<ScourtDownloadSink.fileRecord>();
					int bytesRead;
					using var stream = await response.Content.ReadAsStreamAsync();
					if (fileName.Length == 0)
					{
						Console.WriteLine("failed to download file: server returned no filename");
						continue;
					}
					var tmp = fileName;
					if (!tmp.Contains("!"))
					{
						tmp = $"{tmp}.{fileSize}";
					}
					tmp = $"{tmp}!";//a cheeky little move to make everything work with the code
					while (tmp.Contains("!"))
					{//this part of the code prepares the split points so we can split it into multiple files
						var exclamation_point = tmp.IndexOf("!");
						var dot_point = tmp.LastIndexOf(".", startIndex: exclamation_point);
						ulong fileRecordLength = ulong.Parse(tmp[(dot_point + 1)..(exclamation_point - 0)]);
						// Console.WriteLine($"tmp: {tmp},\texclamation_point: {exclamation_point},\tdot_point: {dot_point},length: {fileRecordLength}");
						var file_name_actual = tmp[..dot_point];
						files.Add(new()
						{
							fs = new FileStream(file_name_actual, FileMode.Create),
							length = fileRecordLength,
							length_remaining = fileRecordLength,
							name = file_name_actual
						});
						tmp = tmp[(exclamation_point+1)..];
					}
					// while (!(files.Count == 0))
					// {
						// long length_remaining = (long) files[0].length;
						while ((bytesRead = await stream.ReadAsync(buffer)) != 0)
						{
							totalBytesRead += (ulong)bytesRead;
							int length_to_modify = (int) Math.Min(files[0].length_remaining, (ulong) bytesRead);
							files[0].length_remaining -= (ulong) length_to_modify;
							await files[0].fs.WriteAsync(buffer.AsMemory(0, length_to_modify));
							Console.Write($"\rDownloading \"{files[0].name}\" {totalBytesRead / 1e0,5:N0} / {files[0].length / 1e0,5:N0} B ({((float)totalBytesRead / files[0].length) * 100,3:N0}%)");
							// if (length_to_modify != bytesRead)
							// {
							// 	//we must switch to the next file, and dump the rest of the buffer...
							// 	files.RemoveAt(0);
							// 	// Console.WriteLine($"changing! {length_to_modify},{bytesRead}");
							// 	Console.WriteLine();
							// 	await files[0].fs.WriteAsync(buffer.AsMemory(length_to_modify, bytesRead - (length_to_modify+0)));
							// 	totalBytesRead = 0;
							// 	continue;
							// }
							if (files[0].length_remaining == 0)
							{
								Console.WriteLine();
								files.RemoveAt(0);
								totalBytesRead = 0;
							}
						}
					Console.WriteLine();
					Console.WriteLine($"Successfully downloaded {fileName}.");
				}
				else
				{
					Console.Write("Failed to download.");
					var body = await response.Content.ReadAsStringAsync();
					switch (body)
					{
						case "-7":
							Console.WriteLine(" Error reading file name. Invalid server product information.");
							break;
						default:
							Console.WriteLine($" (error code {body})");
							break;
					}
				}
			}
			else
			{
				Console.WriteLine($"Failed to download. ({response.StatusCode} {response.ReasonPhrase})");
			}

			context.Response.StatusCode = (int)HttpStatusCode.NoContent;
			context.Response.StatusDescription = "No Content";
			context.Response.ContentLength64 = 0;
			context.Response.OutputStream.Close();
		}
	}

	private static Uri GetDownloadUri(Uri requestUri)
	{
		string? itemId = null;
		string? ott = null;
		string? itoken = null;
		string cmd = "0";

		var segments = requestUri.Segments;
		for (var i = 0; i < segments.Length - 1; i++)
		{
			var keySegment = segments[i].TrimEnd('/');
			var valueSegment = segments[i + 1].TrimEnd('/');
			switch (keySegment)
			{
				case "itemid":
					itemId = valueSegment;
					break;
				case "ott":
					ott = valueSegment;
					break;
				case "itoken":
					itoken = valueSegment;
					break;
				case "cmd":
					cmd = valueSegment;
					break;
			}
		}

		var query = new QueryBuilder(new Dictionary<string, string>
		{
			["itemid"] = itemId,
			["ott"] = ott,
			["itoken"] = itoken,
			["ver"] = "2",
			["cmd"] = "10", //this seems to still work for regular com3d2, so best to just keep it at 10 always.
		});

		var uriBuilder = new UriBuilder
		{
			Host = Host,
			Path = "api/download.php",
			Query = query.ToString(),
		};

		return uriBuilder.Uri;
	}
}
}