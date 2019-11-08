using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using System.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.Primitives;
using System.Net;
using System.Net.Http;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Extensions.Configuration;

namespace boundingboxcroppoc_fa
{
    public static class Function1
    {
        [FunctionName("HttpTriggerCropBoundingBoxInImage")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log, ExecutionContext context)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            //Get Applcation Settings
            var config = new ConfigurationBuilder()
             .SetBasePath(context.FunctionAppDirectory)
             .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
             .AddEnvironmentVariables()
             .Build();

            //Set up link to blob storage for stored images
            var storageConnectionString = config["AzureBlobStorage"]; //Adjust your Application Settings in Azure and to test locally in local.settings.json
            CloudStorageAccount blobAccount = CloudStorageAccount.Parse(storageConnectionString);
            CloudBlobClient blobClient = blobAccount.CreateCloudBlobClient();

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            if (!string.IsNullOrEmpty(requestBody))
            {
                dynamic data = JsonConvert.DeserializeObject(requestBody);

                string sourceName = data.sourceName;
                string sourceContainer = data.sourceContainer;
                string destinationName = data.destinationName;
                string destinationContainer = data.destinationContainer;
                string tag = data.tag;
                int i = 0;

                if (!string.IsNullOrEmpty(sourceName))
                {
                    //Get reference to specific car's container from Car Reg (converts to lower case as container names must be lower case)
                    CloudBlobContainer blobContainer = blobClient.GetContainerReference(sourceContainer.ToLower());
                    //Get reference to image block blob image from ImageFileName parameter the user passed in (images must be in jpg format in the blob service for this to work)
                    CloudBlockBlob cloudBlockBlob = blobContainer.GetBlockBlobReference(sourceName);
                    //Download the image to a stream
                    using (var inputStream = await cloudBlockBlob.OpenReadAsync().ConfigureAwait(false))
                    {
                        using (var streamOut = new MemoryStream())
                        {
                            using (var image = Image.Load(inputStream))
                            {
                                //For each boundingbox, get the rectangle and crop it
                                foreach (var bb in data.boundingBox)
                                {
                                    i += 1;
                                    int orgWidth = image.Width;
                                    int orgHeight = image.Height;

                                    Decimal left = bb.left;
                                    Decimal top = bb.top;
                                    Decimal width = bb.width;
                                    Decimal height = bb.height;

                                    int absleft = Convert.ToInt32(left * orgWidth);
                                    int abstop = Convert.ToInt32(top * orgHeight);
                                    int abswidth = Convert.ToInt32(width * orgWidth);
                                    int absheight = Convert.ToInt32(height * orgHeight);

                                    var clone = image.Clone(x => x.Crop(new Rectangle(absleft, abstop, abswidth, absheight)));
                                    log.LogInformation("Object " + i + " cropped.");

                                    //Upload the stream to Blob Storage
                                    byte[] arr;
                                    clone.SaveAsJpeg(streamOut);
                                    arr = streamOut.GetBuffer();

                                    string filenamedestination = tag + "-" + i + "-" + destinationName;
                                    CloudBlobContainer cloudBlobContainerDest = blobClient.GetContainerReference(destinationContainer.ToLower());
                                    await cloudBlobContainerDest.CreateIfNotExistsAsync();
                                    CloudBlockBlob cloudBlockBlobDest = cloudBlobContainerDest.GetBlockBlobReference(filenamedestination);
                                    streamOut.Seek(0, SeekOrigin.Begin);
                                    await cloudBlockBlobDest.UploadFromStreamAsync(streamOut);
                                }
                                log.LogInformation("Finished image.");
                                //Create the Http response message with the cropped image
                                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
                                return (ActionResult)new OkObjectResult("All crops uploaded to Blob storage");
                            }
                        }
                    }
                }

                else
                {
                    HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.BadRequest);
                    return new BadRequestObjectResult("Please pass a valid source url in the request body.");
                }
            }
            else
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.BadRequest);
                return new BadRequestObjectResult("Please pass a valid request body.");
            }
        }
    }
}
