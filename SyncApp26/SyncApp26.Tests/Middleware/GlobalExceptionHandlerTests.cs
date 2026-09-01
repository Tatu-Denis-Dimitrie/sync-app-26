using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SyncApp26.API.Middleware;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Middleware
{
    public class GlobalExceptionHandlerTests
    {
        private readonly Mock<ILogger<GlobalExceptionHandler>> _loggerMock = new();
        private readonly Mock<IProblemDetailsService> _problemDetailsServiceMock = new();

        private GlobalExceptionHandler CreateHandler() =>
            new(_loggerMock.Object, _problemDetailsServiceMock.Object);

        private static DefaultHttpContext CreateHttpContext() =>
            new() { RequestServices = RealLocalizerFactory.ServiceProvider() };

        [Fact]
        public async Task TryHandleAsync_LogsTheException()
        {
            var handler = CreateHandler();
            var httpContext = CreateHttpContext();
            var exception = new InvalidOperationException("connection string is invalid: user=admin;password=hunter2");

            await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

            _loggerMock.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    exception,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task TryHandleAsync_SetsInternalServerErrorStatusCode()
        {
            var handler = CreateHandler();
            var httpContext = CreateHttpContext();

            await handler.TryHandleAsync(httpContext, new Exception("boom"), CancellationToken.None);

            Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task TryHandleAsync_NeverExposesTheExceptionMessageInTheResponseBody()
        {
            ProblemDetailsContext? captured = null;
            _problemDetailsServiceMock
                .Setup(s => s.WriteAsync(It.IsAny<ProblemDetailsContext>()))
                .Callback<ProblemDetailsContext>(ctx => captured = ctx)
                .Returns(ValueTask.CompletedTask);

            var handler = CreateHandler();
            var httpContext = CreateHttpContext();
            var secret = "connection string is invalid: user=admin;password=hunter2";

            await handler.TryHandleAsync(httpContext, new InvalidOperationException(secret), CancellationToken.None);

            Assert.NotNull(captured);
            var problemDetails = captured!.ProblemDetails;
            Assert.DoesNotContain(secret, problemDetails.Title ?? string.Empty);
            Assert.DoesNotContain(secret, problemDetails.Detail ?? string.Empty);
            Assert.Contains("traceId", problemDetails.Extensions.Keys);
        }

        [Fact]
        public async Task TryHandleAsync_ReturnsTrueSoTheResponseIsConsideredHandled()
        {
            _problemDetailsServiceMock
                .Setup(s => s.WriteAsync(It.IsAny<ProblemDetailsContext>()))
                .Returns(ValueTask.CompletedTask);

            var handler = CreateHandler();
            var httpContext = CreateHttpContext();

            var handled = await handler.TryHandleAsync(httpContext, new Exception("boom"), CancellationToken.None);

            Assert.True(handled);
        }
    }
}
