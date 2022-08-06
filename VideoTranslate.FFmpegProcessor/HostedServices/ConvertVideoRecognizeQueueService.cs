using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Text;
using VideoTranslate.Shared.DTO.Configuration;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client.Events;
using Newtonsoft.Json;
using VideoTranslate.Shared.DTO.MQModels;
using VideoTranslate.Shared.Abstractions.Services;

namespace VideoTranslate.FFmpegProcessor.HostedServices
{
    public class ConvertVideoRecognizeQueueService : IHostedService
    {
        private readonly ILogger<ConvertVideoRecognizeQueueService> logger;
        private readonly RabbitMQConfiguration rabbitMQConfiguration;
        private readonly IVideoFileService videoFileService;
        private readonly IFileService fileService;

        private ConnectionFactory connectionFactory;
        private IConnection connection;
        private IModel channel;
        private string QueueName = "ffmpeg_convert_for_recognition";

        public ConvertVideoRecognizeQueueService(
            ILogger<ConvertVideoRecognizeQueueService> logger,
            RabbitMQConfiguration rabbitMQConfiguration,
            IVideoFileService videoFileService,
            IFileService fileService)
        {
            this.logger = logger;
            this.rabbitMQConfiguration = rabbitMQConfiguration;
            this.videoFileService = videoFileService;
            this.fileService = fileService;

            this.connectionFactory = new ConnectionFactory()
            {
                HostName = this.rabbitMQConfiguration.HostName,
                // port = 5672, default value
                //VirtualHost = "/",
                UserName = this.rabbitMQConfiguration.User,
                Password = this.rabbitMQConfiguration.Password
            };

            this.connection = this.connectionFactory.CreateConnection();
            this.channel = this.connection.CreateModel();
        }

        // Initiate RabbitMQ and start listening to an input queue
        private void Run()
        {
            // A queue to read messages
            this.channel.QueueDeclare(queue: this.QueueName,
                                durable: false,
                                exclusive: false,
                                autoDelete: false,
                                arguments: null);
            // A queue to write messages
            this.logger.LogInformation(" [*] Waiting for messages.");

            var consumer = new EventingBasicConsumer(this.channel);
            consumer.Received += OnMessageRecieved;

            this.channel.BasicConsume(queue: this.QueueName,
                                autoAck: false,
                                consumer: consumer);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.Run();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            this.channel.Dispose();
            this.connection.Dispose();
            return Task.CompletedTask;
        }

        private void OnMessageRecieved(object? model, BasicDeliverEventArgs args)
        {
            var body = args.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            this.logger.LogInformation(" [x] Received {0}", message);
            var convertVideoRecognizeCommand = JsonConvert.DeserializeObject<ConvertVideoRecognizeCommand>(message);
            if (convertVideoRecognizeCommand == null)
            {
                this.logger.LogError("Cannot deserialize object convertVideoRecognizeCommand.");
            }
            else
            {
                this.logger.LogInformation($"processing VideoInfoId: {convertVideoRecognizeCommand.VideoInfoId}");

                var videoInfoFile = this.videoFileService.GetOriginalVideoByVideoInfoId(convertVideoRecognizeCommand.VideoInfoId);

                var fileModel = this.fileService.GetById(videoInfoFile.FileId);


                this.channel.BasicAck(deliveryTag: args.DeliveryTag, multiple: false);
            }
        }
    }
}
