using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using OmniForge.Core.Entities;
using OmniForge.Core.Interfaces;

namespace OmniForge.Infrastructure.Repositories
{
    public class TemplateRepository : ITemplateRepository
    {
        private readonly TableClient _usersClient;

        public TemplateRepository(TableServiceClient tableServiceClient)
        {
            _usersClient = tableServiceClient.GetTableClient("users");
        }

        public async Task InitializeAsync()
        {
            await _usersClient.CreateIfNotExistsAsync();
        }

        public Task<Dictionary<string, Template>> GetAvailableTemplatesAsync()
        {
            // Return hardcoded templates as per Node.js implementation
            return Task.FromResult(new Dictionary<string, Template>
            {
                { "asylum_themed", new Template
                    {
                        Name = "Asylum Themed",
                        Description = "Dark horror-themed template with blood effects and creepy animations",
                        Type = "built-in",
                        Config = new TemplateConfig
                        {
                            Colors = new TemplateColors { Primary = "#8B0000", Secondary = "#DC143C", Background = "#1a0000", Text = "#FFFFFF", Accent = "#FF6B6B" },
                            Fonts = new TemplateFonts { Primary = "Creepster, cursive", Secondary = "Arial, sans-serif" },
                            Animations = new TemplateAnimations { BloodDrip = true, Screenshake = true, FadeEffects = true, ParticleEffects = true },
                            Sounds = new TemplateSounds { Death = "scream.mp3", Swear = "bleep.mp3", Milestone = "creepy_bell.mp3" }
                        }
                    }
                },
                { "modern_minimal", new Template
                    {
                        Name = "Modern Minimal",
                        Description = "Clean, modern design with smooth animations",
                        Type = "built-in",
                        Config = new TemplateConfig
                        {
                            Colors = new TemplateColors { Primary = "#6366f1", Secondary = "#8b5cf6", Background = "#ffffff", Text = "#1f2937", Accent = "#3b82f6" },
                            Fonts = new TemplateFonts { Primary = "Inter, sans-serif", Secondary = "SF Pro Display, sans-serif" },
                            Animations = new TemplateAnimations { SlideIn = true, FadeEffects = true, BounceOnUpdate = true, Glassmorphism = true },
                            Sounds = new TemplateSounds { Death = "notification.mp3", Swear = "pop.mp3", Milestone = "achievement.mp3" }
                        }
                    }
                },
                { "streamer_pro", new Template
                    {
                        Name = "Streamer Pro",
                        Description = "Professional streaming template with customizable colors",
                        Type = "built-in",
                        Config = new TemplateConfig
                        {
                            Colors = new TemplateColors { Primary = "#9146ff", Secondary = "#772ce8", Background = "rgba(0, 0, 0, 0.8)", Text = "#ffffff", Accent = "#00f5ff" },
                            Fonts = new TemplateFonts { Primary = "Roboto, sans-serif", Secondary = "Open Sans, sans-serif" },
                            Animations = new TemplateAnimations { SlideIn = true, Typewriter = true, NeonGlow = true, ParticleTrails = true },
                            Sounds = new TemplateSounds { Death = "game_over.mp3", Swear = "censored.mp3", Milestone = "level_up.mp3" }
                        }
                    }
                }
            });
        }

        public async Task<Template?> GetUserCustomTemplateAsync(string userId)
        {
            try
            {
                var response = await _usersClient.GetEntityAsync<TableEntity>(userId, "customTemplate");
                if (response.Value.TryGetValue("templateConfig", out var configObj) && configObj is string configJson)
                {
                    return JsonSerializer.Deserialize<Template>(configJson);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // No custom template found
            }

            return null;
        }

        public async Task SaveUserCustomTemplateAsync(string userId, Template template)
        {
            var entity = new TableEntity(userId, "customTemplate")
            {
                ["templateConfig"] = JsonSerializer.Serialize(template),
                ["lastUpdated"] = DateTimeOffset.UtcNow
            };

            await _usersClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }
    }
}
