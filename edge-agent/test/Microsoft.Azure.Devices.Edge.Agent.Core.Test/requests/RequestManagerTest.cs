// Copyright (c) Microsoft. All rights reserved.
namespace Microsoft.Azure.Devices.Edge.Agent.Core.Test.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Agent.Core.Requests;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Test.Common;
    using Moq;
    using Newtonsoft.Json.Linq;
    using Xunit;

    [Unit]
    public class RequestManagerTest
    {
        public static IEnumerable<object[]> GetProcessRequestWithException()
        {
            yield return new object[] { "{\"prop2\":\"foo\",\"prop1\":100}", 400, new ArgumentNullException() };

            yield return new object[] { "{\"prop2\":\"foo\",\"prop1\":100}", 400, new ArgumentException() };

            yield return new object[] { "{\"prop2\":\"foo\",\"prop1\":100}", 500, new InvalidOperationException() };
        }

        [Fact]
        public async Task TestProcessRequest()
        {
            // Arrange
            var requestHandler = new Mock<IRequestHandler>();
            requestHandler.Setup(r => r.HandleRequest(It.IsAny<Option<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Option.Some("{\"prop3\":\"foo\",\"prop4\":100}"));
            requestHandler.SetupGet(r => r.RequestName).Returns("req1");
            var requestHandlers = new List<IRequestHandler>
            {
                requestHandler.Object
            };
            var requestManager = new RequestManager(requestHandlers, TimeSpan.FromSeconds(60));
            string payload = "{\"prop2\":\"foo\",\"prop1\":100}";

            // Act
            (int responseStatus, Option<string> responsePayload) = await requestManager.ProcessRequest("req", payload);

            // Assert
            Assert.Equal(400, responseStatus);
            Assert.True(responsePayload.HasValue);
            JObject parsedJson = JObject.Parse(responsePayload.OrDefault());
            Assert.False(string.IsNullOrWhiteSpace(parsedJson["message"].ToString()));

            // Act
            (responseStatus, responsePayload) = await requestManager.ProcessRequest(string.Empty, payload);

            // Assert
            Assert.Equal(400, responseStatus);
            Assert.True(responsePayload.HasValue);
            parsedJson = JObject.Parse(responsePayload.OrDefault());
            Assert.False(string.IsNullOrWhiteSpace(parsedJson["message"].ToString()));

            // Act
            (responseStatus, responsePayload) = await requestManager.ProcessRequest(null, payload);

            // Assert
            Assert.Equal(400, responseStatus);
            Assert.True(responsePayload.HasValue);
            parsedJson = JObject.Parse(responsePayload.OrDefault());
            Assert.False(string.IsNullOrWhiteSpace(parsedJson["message"].ToString()));

            // Act
            (responseStatus, responsePayload) = await requestManager.ProcessRequest("req1", payload);

            // Assert
            Assert.Equal(200, responseStatus);
            Assert.Equal("{\"prop3\":\"foo\",\"prop4\":100}", responsePayload.OrDefault());

            // Act - Test for case sensitivity
            (responseStatus, responsePayload) = await requestManager.ProcessRequest("ReQ1", payload);

            // Assert
            Assert.Equal(200, responseStatus);
            Assert.Equal("{\"prop3\":\"foo\",\"prop4\":100}", responsePayload.OrDefault());
        }

        [Theory]
        [MemberData(nameof(GetProcessRequestWithException))]
        public async Task TestProcessRequestWithHandlerException(string payload, int expectedStatus, Exception handlerException)
        {
            // Arrange
            var requestHandler = new Mock<IRequestHandler>();
            requestHandler.Setup(r => r.HandleRequest(Option.Some(payload), It.IsAny<CancellationToken>())).ThrowsAsync(handlerException);
            requestHandler.SetupGet(r => r.RequestName).Returns("req1");
            var requestHandlers = new List<IRequestHandler>
            {
                requestHandler.Object
            };
            var requestManager = new RequestManager(requestHandlers, TimeSpan.FromSeconds(60));

            // Act
            (int responseStatus, Option<string> responsePayload) = await requestManager.ProcessRequest("req1", payload);

            // Assert
            Assert.Equal(expectedStatus, responseStatus);
            Assert.True(responsePayload.HasValue);
            JObject parsedJson = JObject.Parse(responsePayload.OrDefault());
            Assert.False(string.IsNullOrWhiteSpace(parsedJson["message"].ToString()));
        }

        [Fact]
        public async Task TestRequestCancelled()
        {
            // Arrange
            var requestHandler = new Mock<IRequestHandler>();
            requestHandler.Setup(r => r.HandleRequest(It.IsAny<Option<string>>(), It.IsAny<CancellationToken>()))
                .Callback<Option<string>, CancellationToken>((s, c) => Task.Delay(TimeSpan.FromSeconds(60), c).Wait(c))
                .ReturnsAsync(Option.Some("{\"prop3\":\"foo\",\"prop4\":100}"));

            requestHandler.SetupGet(r => r.RequestName).Returns("req1");
            var requestHandlers = new List<IRequestHandler>
            {
                requestHandler.Object
            };
            var requestManager = new RequestManager(requestHandlers, TimeSpan.FromSeconds(5));
            string payload = "{\"prop2\":\"foo\",\"prop1\":100}";

            // Act
            Task<(int responseStatus, Option<string> responsePayload)> processRequestTask = requestManager.ProcessRequest("req1", payload);
            Task delayTask = Task.Delay(TimeSpan.FromSeconds(10));
            Task completedTask = await Task.WhenAny(processRequestTask, delayTask);

            // Assert
            Assert.Equal(completedTask, processRequestTask);
            (int responseStatus, Option<string> _) = await processRequestTask;
            Assert.Equal(500, responseStatus);
        }
    }
}
