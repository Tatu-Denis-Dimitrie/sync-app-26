using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Documents
{
    public class SignatureAnomalyAlertControllerTests
    {
        private readonly Mock<ISignatureAnomalyAlertRepository> _alertRepositoryMock = new();

        private SignatureAnomalyAlertController CreateController(Guid? callerId = null, string role = Roles.Admin)
        {
            var controller = new SignatureAnomalyAlertController(_alertRepositoryMock.Object);
            controller.SetUser(callerId ?? Guid.NewGuid(), role: role);
            return controller;
        }

        private static SignatureAnomalyAlert MakeAlert(int recordsChecked = 10, int anomaliesFound = 2) => new()
        {
            Id = Guid.NewGuid(),
            RecordsChecked = recordsChecked,
            AnomaliesFound = anomaliesFound,
            OccurredAt = DateTimeOffset.UtcNow
        };

        [Fact]
        public async Task GetUnread_ReturnsAlertsFromRepository()
        {
            var controller = CreateController();
            var alert = MakeAlert();
            _alertRepositoryMock.Setup(r => r.GetUnreadAsync()).ReturnsAsync(new List<SignatureAnomalyAlert> { alert });

            var result = await controller.GetUnread();

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<SignatureAnomalyAlertDTO>>(ok.Value).ToList();
            var dto = Assert.Single(list);
            Assert.Equal(alert.Id, dto.Id);
            Assert.Equal(alert.RecordsChecked, dto.RecordsChecked);
            Assert.Equal(alert.AnomaliesFound, dto.AnomaliesFound);
        }

        [Fact]
        public async Task GetUnread_NoAlerts_ReturnsEmptyList()
        {
            var controller = CreateController();
            _alertRepositoryMock.Setup(r => r.GetUnreadAsync()).ReturnsAsync(new List<SignatureAnomalyAlert>());

            var result = await controller.GetUnread();

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<SignatureAnomalyAlertDTO>>(ok.Value);
            Assert.Empty(list);
        }

        [Fact]
        public async Task DismissAll_MarksAllReadForCaller()
        {
            var callerId = Guid.NewGuid();
            var controller = CreateController(callerId);

            var result = await controller.DismissAll();

            Assert.IsType<NoContentResult>(result);
            _alertRepositoryMock.Verify(r => r.MarkAllAsReadAsync(callerId), Times.Once);
        }

        [Fact]
        public async Task DismissAll_NoUserClaim_ReturnsUnauthorized()
        {
            var controller = CreateController();
            controller.SetAnonymousUser();

            var result = await controller.DismissAll();

            Assert.IsType<UnauthorizedResult>(result);
            _alertRepositoryMock.Verify(r => r.MarkAllAsReadAsync(It.IsAny<Guid>()), Times.Never);
        }
    }
}
