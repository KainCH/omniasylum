using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;
using OmniForge.Web.Controllers;
using Xunit;

namespace OmniForge.Tests
{
    public class SeriesControllerTests
    {
        private readonly Mock<ISeriesRepository> _mockSeriesRepository;
        private readonly Mock<ICounterRepository> _mockCounterRepository;
        private readonly Mock<IOverlayNotifier> _mockOverlayNotifier;
        private readonly SeriesController _controller;

        public SeriesControllerTests()
        {
            _mockSeriesRepository = new Mock<ISeriesRepository>();
            _mockCounterRepository = new Mock<ICounterRepository>();
            _mockOverlayNotifier = new Mock<IOverlayNotifier>();

            _controller = new SeriesController(
                _mockSeriesRepository.Object,
                _mockCounterRepository.Object,
                _mockOverlayNotifier.Object);

            // Setup User Context
            var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim("userId", "12345")
            }, "mock"));

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };
        }

        [Fact]
        public async Task GetSeries_ShouldReturnOk_WhenUserAuthenticated()
        {
            var seriesList = new List<Series>
            {
                new Series
                {
                    Id = "s1",
                    Name = "Run 1",
                    UserId = "12345",
                    Snapshot = new Counter { Deaths = 10, Swears = 5, Screams = 3 }
                }
            };

            _mockSeriesRepository.Setup(x => x.GetSeriesAsync("12345"))
                .ReturnsAsync(seriesList);

            var result = await _controller.GetSeries();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
        }

        [Fact]
        public async Task SaveSeries_ShouldReturnOk_WhenValidRequest()
        {
            var request = new SaveSeriesRequest
            {
                SeriesName = "Elden Ring",
                Description = "No hit run"
            };

            var counters = new Counter { Deaths = 10, Swears = 5, Screams = 2 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counters);

            var result = await _controller.SaveSeries(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(x => x.CreateSeriesAsync(It.Is<Series>(s =>
                s.Name == "Elden Ring" &&
                s.UserId == "12345" &&
                s.Snapshot.Deaths == 10 &&
                s.Snapshot.Swears == 5 &&
                s.Snapshot.Screams == 2)), Times.Once);
        }

        [Fact]
        public async Task SaveSeries_ShouldReturnBadRequest_WhenSeriesNameEmpty()
        {
            var request = new SaveSeriesRequest
            {
                SeriesName = "",
                Description = "Test"
            };

            var result = await _controller.SaveSeries(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SaveSeries_ShouldUpdateExistingSeries_WhenSeriesIdProvided()
        {
            var existingSeries = new Series
            {
                Id = "existing-id",
                UserId = "12345",
                Name = "Old Name",
                Snapshot = new Counter { Deaths = 5 }
            };

            var request = new SaveSeriesRequest
            {
                SeriesId = "existing-id",
                SeriesName = "Updated Name",
                Description = "Updated description"
            };

            var counters = new Counter { Deaths = 20, Swears = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counters);
            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", "existing-id"))
                .ReturnsAsync(existingSeries);

            var result = await _controller.SaveSeries(request);

            Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(x => x.CreateSeriesAsync(It.IsAny<Series>()), Times.Never);
            _mockSeriesRepository.Verify(x => x.UpdateSeriesAsync(It.Is<Series>(s =>
                s.Name == "Updated Name" &&
                s.Snapshot.Deaths == 20)), Times.Once);
        }

        [Fact]
        public async Task LoadSeries_ShouldReturnOk_WhenSeriesExists()
        {
            var seriesId = "s1";
            var snapshot = new Counter { Deaths = 50, Swears = 25, Screams = 10 };
            var series = new Series { Id = seriesId, Name = "Test Series", Snapshot = snapshot };

            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync(series);

            var request = new LoadSeriesRequest { SeriesId = seriesId };
            var result = await _controller.LoadSeries(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c =>
                c.Deaths == 50 &&
                c.Swears == 25 &&
                c.Screams == 10 &&
                c.TwitchUserId == "12345")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", It.IsAny<Counter>()), Times.Once);
        }

        [Fact]
        public async Task LoadSeries_ShouldReturnNotFound_WhenSeriesDoesNotExist()
        {
            var seriesId = "s1";
            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync((Series?)null);

            var request = new LoadSeriesRequest { SeriesId = seriesId };
            var result = await _controller.LoadSeries(request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task LoadSeries_ShouldReturnBadRequest_WhenSeriesIdEmpty()
        {
            var request = new LoadSeriesRequest { SeriesId = "" };
            var result = await _controller.LoadSeries(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task UpdateSeries_ShouldReturnOk_WhenSeriesExists()
        {
            var seriesId = "s1";
            var existingSeries = new Series
            {
                Id = seriesId,
                UserId = "12345",
                Name = "Old Name",
                Snapshot = new Counter { Deaths = 5 }
            };

            var counters = new Counter { Deaths = 30, Swears = 15, Screams = 8 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counters);
            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync(existingSeries);

            var request = new UpdateSeriesRequest
            {
                SeriesName = "New Name",
                Description = "New description"
            };

            var result = await _controller.UpdateSeries(seriesId, request);

            Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(x => x.UpdateSeriesAsync(It.Is<Series>(s =>
                s.Name == "New Name" &&
                s.Snapshot.Deaths == 30)), Times.Once);
        }

        [Fact]
        public async Task UpdateSeries_ShouldReturnNotFound_WhenSeriesDoesNotExist()
        {
            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", "nonexistent"))
                .ReturnsAsync((Series?)null);

            var request = new UpdateSeriesRequest { SeriesName = "Test" };
            var result = await _controller.UpdateSeries("nonexistent", request);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task OverwriteSeries_ShouldOverwriteSnapshot_WithoutChangingNameOrDescription()
        {
            var seriesId = "s1";
            var existingSeries = new Series
            {
                Id = seriesId,
                UserId = "12345",
                Name = "My Series",
                Description = "My Description",
                Snapshot = new Counter { Deaths = 5 }
            };

            var counters = new Counter { Deaths = 30, Swears = 15, Screams = 8 };

            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync(existingSeries);
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counters);

            var result = await _controller.OverwriteSeries(seriesId);

            Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(x => x.UpdateSeriesAsync(It.Is<Series>(s =>
                s.Id == seriesId &&
                s.Name == "My Series" &&
                s.Description == "My Description" &&
                s.Snapshot.Deaths == 30 &&
                s.Snapshot.Swears == 15 &&
                s.Snapshot.Screams == 8
            )), Times.Once);
        }

        [Fact]
        public async Task OverwriteSeries_ShouldReturnNull_WhenSeriesDoesNotExist_AndNotLoadCounters()
        {
            var seriesId = "missing";

            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync((Series?)null);

            var result = await _controller.OverwriteSeries(seriesId);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Null(ok.Value);
            _mockCounterRepository.Verify(x => x.GetCountersAsync(It.IsAny<string>()), Times.Never);
            _mockSeriesRepository.Verify(x => x.UpdateSeriesAsync(It.IsAny<Series>()), Times.Never);
        }

        [Fact]
        public async Task DeleteSeries_ShouldReturnOk_WhenSeriesExists()
        {
            var seriesId = "s1";
            var existingSeries = new Series { Id = seriesId, UserId = "12345", Name = "Test" };

            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync(existingSeries);

            var result = await _controller.DeleteSeries(seriesId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(x => x.DeleteSeriesAsync("12345", seriesId), Times.Once);
        }

        [Fact]
        public async Task DeleteSeries_ShouldReturnNotFound_WhenSeriesDoesNotExist()
        {
            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", "nonexistent"))
                .ReturnsAsync((Series?)null);

            var result = await _controller.DeleteSeries("nonexistent");

            Assert.IsType<NotFoundObjectResult>(result);
        }
    }
}
