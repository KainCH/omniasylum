using System;

namespace OmniForge.Core.Entities
{
    public class Template
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = "built-in";
        public TemplateConfig Config { get; set; } = new TemplateConfig();
        public string? TemplateStyle { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    public class TemplateConfig
    {
        public TemplateColors Colors { get; set; } = new TemplateColors();
        public TemplateFonts Fonts { get; set; } = new TemplateFonts();
        public TemplateAnimations Animations { get; set; } = new TemplateAnimations();
        public TemplateSounds Sounds { get; set; } = new TemplateSounds();
    }

    public class TemplateColors
    {
        public string Primary { get; set; } = string.Empty;
        public string Secondary { get; set; } = string.Empty;
        public string Background { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Accent { get; set; } = string.Empty;
    }

    public class TemplateFonts
    {
        public string Primary { get; set; } = string.Empty;
        public string Secondary { get; set; } = string.Empty;
    }

    public class TemplateAnimations
    {
        public bool BloodDrip { get; set; }
        public bool Screenshake { get; set; }
        public bool FadeEffects { get; set; }
        public bool ParticleEffects { get; set; }
        public bool SlideIn { get; set; }
        public bool BounceOnUpdate { get; set; }
        public bool Glassmorphism { get; set; }
        public bool Typewriter { get; set; }
        public bool NeonGlow { get; set; }
        public bool ParticleTrails { get; set; }
    }

    public class TemplateSounds
    {
        public string Death { get; set; } = string.Empty;
        public string Swear { get; set; } = string.Empty;
        public string Milestone { get; set; } = string.Empty;
    }
}
