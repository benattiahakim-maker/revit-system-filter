using System.Net;
using Egis.Revit.SystemFilter.Aps.Oss;

namespace Egis.Revit.SystemFilter.Aps.Tests;

public sealed class OssClientTests
{
    private const string Object = $"{Support.Oss}/buckets/egr-bucket/objects/input%2Fmodel.zip";

    [Fact]
    public async Task Bucket_creation_tolerates_existing_bucket()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Post, $"{Support.Oss}/buckets", HttpStatusCode.OK, """{"bucketKey":"egr-bucket"}""");
        handler.Expect(HttpMethod.Post, $"{Support.Oss}/buckets", HttpStatusCode.Conflict, """{"reason":"Bucket already exists"}""");
        var oss = Client(http);

        Assert.True(await oss.CreateBucketAsync("egr-bucket", region: "US"));
        Assert.False(await oss.CreateBucketAsync("egr-bucket"));

        Assert.Equal("""{"bucketKey":"egr-bucket","policyKey":"transient"}""", handler.Requests[0].BodyText);
        Assert.Equal("US", handler.Requests[0].Headers["x-ads-region"]);
        Assert.Equal($"Bearer {Support.Token}", handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task Upload_uses_signed_url_without_token_then_completes()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Object}/signeds3upload?parts=1&minutesExpiration=60", HttpStatusCode.OK,
            """{"uploadKey":"UK1","urls":["https://s3.test/part1?X-Amz-Signature=s"]}""");
        handler.Expect(HttpMethod.Put, "https://s3.test/part1?X-Amz-Signature=s", HttpStatusCode.OK);
        handler.Expect(HttpMethod.Post, $"{Object}/signeds3upload", HttpStatusCode.OK, """{"objectId":"urn:adsk.objects:os.object:egr-bucket/input/model.zip","size":4}""");
        var data = "ABCD"u8.ToArray();

        var result = await Client(http).UploadObjectAsync("egr-bucket", "input/model.zip", new MemoryStream(data), data.Length);

        Assert.Equal(4, result!["size"]!.GetValue<int>());
        Assert.Null(handler.Requests[1].Authorization);
        Assert.Equal(data, handler.Requests[1].Body);
        Assert.Equal("""{"uploadKey":"UK1"}""", handler.Requests[2].BodyText);
        Assert.Equal($"Bearer {Support.Token}", handler.Requests[2].Authorization);
    }

    [Fact]
    public async Task Large_upload_is_split_in_parts()
    {
        const int partSize = 5 * 1024 * 1024;
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Object}/signeds3upload?parts=2&minutesExpiration=60", HttpStatusCode.OK,
            """{"uploadKey":"UK2","urls":["https://s3.test/p1","https://s3.test/p2"]}""");
        handler.Expect(HttpMethod.Put, "https://s3.test/p1", HttpStatusCode.OK);
        handler.Expect(HttpMethod.Put, "https://s3.test/p2", HttpStatusCode.OK);
        handler.Expect(HttpMethod.Post, $"{Object}/signeds3upload", HttpStatusCode.OK, "{}");
        var data = new byte[partSize + 10];
        data[^1] = 42;

        await Client(http).UploadObjectAsync("egr-bucket", "input/model.zip", new MemoryStream(data), data.Length, partSize);

        Assert.Equal(partSize, handler.Requests[1].Body.Length);
        Assert.Equal(10, handler.Requests[2].Body.Length);
        Assert.Equal(42, handler.Requests[2].Body[^1]);
        Assert.Equal("""{"uploadKey":"UK2"}""", handler.Requests[3].BodyText);
    }

    [Fact]
    public async Task Signed_urls_for_workitem_arguments()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Object}/signeds3download?minutesExpiration=60", HttpStatusCode.OK, """{"status":"complete","url":"https://s3.test/get?sig=1"}""");
        handler.Expect(HttpMethod.Get, $"{Support.Oss}/buckets/egr-bucket/objects/result.json/signeds3upload?parts=1&minutesExpiration=30", HttpStatusCode.OK,
            """{"uploadKey":"UK3","urls":["https://s3.test/put?sig=2"]}""");
        var oss = Client(http);

        Assert.Equal("https://s3.test/get?sig=1", await oss.GetSignedDownloadUrlAsync("egr-bucket", "input/model.zip", minutesExpiration: 120));
        var upload = await oss.GetSignedUploadAsync("egr-bucket", "result.json", minutesExpiration: 30);

        Assert.Equal("UK3", upload.UploadKey);
        Assert.Equal(["https://s3.test/put?sig=2"], upload.Urls);
        Assert.DoesNotContain("sig=2", upload.ToString());
    }

    [Fact]
    public async Task Download_reads_object_through_signed_url()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Support.Oss}/buckets/egr-bucket/objects/result.json/signeds3download?minutesExpiration=60", HttpStatusCode.OK,
            """{"url":"https://s3.test/get?sig=3"}""");
        handler.Expect(HttpMethod.Get, "https://s3.test/get?sig=3", HttpStatusCode.OK, """{"status":"succeeded"}""");

        var bytes = await Client(http).DownloadObjectAsync("egr-bucket", "result.json");

        Assert.Equal("""{"status":"succeeded"}""", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Null(handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task Failed_part_upload_hides_signed_url()
    {
        var (handler, http) = Support.Http();
        handler.Expect(HttpMethod.Get, $"{Object}/signeds3upload?parts=1&minutesExpiration=60", HttpStatusCode.OK,
            """{"uploadKey":"UK1","urls":["https://s3.test/part1?X-Amz-Signature=topsecret"]}""");
        handler.Expect(HttpMethod.Put, "https://s3.test/part1?X-Amz-Signature=topsecret", HttpStatusCode.Forbidden, "SignatureDoesNotMatch");

        var error = await Assert.ThrowsAsync<ApsException>(() => Client(http).UploadObjectAsync("egr-bucket", "input/model.zip", new MemoryStream([1]), 1));

        Assert.Equal("PUT <URL signée S3, partie 1/1> : HTTP 403 SignatureDoesNotMatch", error.Message);
    }

    private static OssClient Client(HttpClient http) => new(http, new StaticTokenProvider(Support.Token), Support.Oss);
}
