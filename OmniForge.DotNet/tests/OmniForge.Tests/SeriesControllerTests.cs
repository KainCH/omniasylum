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
                new Series { Id = "s1", Name = "Run 1" }
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

            var counters = new Counter { Deaths = 10 };
            _mockCounterRepository.Setup(x => x.GetCountersAsync("12345"))
                .ReturnsAsync(counters);

            var result = await _controller.SaveSeries(request);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(x => x.CreateSeriesAsync(It.Is<Series>(s =>
                s.Name == "Elden Ring" &&
                s.UserId == "12345" &&
                s.Snapshot.Deaths == 10)), Times.Once);
        }

        [Fact]
        public async Task LoadSeries_ShouldReturnOk_WhenSeriesExists()
        {
            var seriesId = "s1";
            var snapshot = new Counter { Deaths = 50 };
            var series = new Series { Id = seriesId, Snapshot = snapshot };

            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync(series);

            var result = await _controller.LoadSeries(seriesId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockCounterRepository.Verify(x => x.SaveCountersAsync(It.Is<Counter>(c => c.Deaths == 50 && c.TwitchUserId == "12345")), Times.Once);
            _mockOverlayNotifier.Verify(x => x.NotifyCounterUpdateAsync("12345", It.IsAny<Counter>()), Times.Once);
        }

        [Fact]
        public async Task LoadSeries_ShouldReturnNotFound_WhenSeriesDoesNotExist()
        {
            var seriesId = "s1";
            _mockSeriesRepository.Setup(x => x.GetSeriesByIdAsync("12345", seriesId))
                .ReturnsAsync((Series?)null);

            var result = await _controller.LoadSeries(seriesId);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task DeleteSeries_ShouldReturnOk()
        {
            var seriesId = "s1";

            var result = await _controller.DeleteSeries(seriesId);

            var okResult = Assert.IsType<OkObjectResult>(result);
            _mockSeriesRepository.Verify(x => x.DeleteSeriesAsync("12345", seriesId), Times.Once);
        }
    }
}
