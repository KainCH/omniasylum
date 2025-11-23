window.overlayInterop = {
    init: function() {
        if (window.asylumEffects) {
            console.log("AsylumEffects initialized via Interop");
        } else {
            console.error("AsylumEffects not found!");
        }
    },

    triggerAlert: function(type, payload) {
        console.log("Triggering alert:", type, payload);

        // Handle specific types that need custom logic
        if (type === 'bits') {
            this.triggerBitsCelebration();
        } else if (type === 'subscription' || type === 'resub' || type === 'giftsub') {
            this.triggerSubCelebration();
            if (payload.textPrompt) {
                this.showSubBanner(payload.textPrompt);
            }
        }

        // Use AsylumEffects for the main alert popup
        if (window.asylumEffects) {
            window.asylumEffects.triggerEffect({
                textPrompt: payload.textPrompt || payload.name,
                backgroundColor: payload.backgroundColor,
                textColor: payload.textColor,
                borderColor: payload.borderColor,
                duration: payload.duration || 5000,
                soundTrigger: payload.sound,
                effects: payload.effects || {}
            });
        }
    },

    triggerBitsCelebration: function() {
        const container = document.getElementById('bits-celebration');
        if (!container) return;

        for (let i = 0; i < 50; i++) {
            const particle = document.createElement('div');
            particle.className = 'bit-particle';
            particle.style.left = Math.random() * 100 + 'vw';
            particle.style.animationDuration = (Math.random() * 2 + 2) + 's';
            particle.style.opacity = Math.random();
            container.appendChild(particle);

            setTimeout(() => {
                particle.remove();
            }, 4000);
        }
    },

    triggerSubCelebration: function() {
        const container = document.getElementById('sub-celebration');
        if (!container) return;

        for (let i = 0; i < 20; i++) {
            const heart = document.createElement('div');
            heart.className = 'sub-heart';
            heart.innerHTML = '💜';
            heart.style.left = Math.random() * 100 + 'vw';
            heart.style.animationDelay = Math.random() * 2 + 's';
            container.appendChild(heart);

            setTimeout(() => {
                heart.remove();
            }, 5000);
        }
    },

    showSubBanner: function(text) {
        const banner = document.getElementById('sub-banner');
        if (!banner) return;

        banner.textContent = text;
        banner.classList.remove('hide');
        banner.classList.add('show');

        setTimeout(() => {
            banner.classList.remove('show');
            banner.classList.add('hide');
        }, 5000);
    },

    triggerDeath: function(newCount) {
        if (window.asylumEffects) {
            window.asylumEffects.triggerEffect({
                textPrompt: `DEATH COUNT: ${newCount}`,
                backgroundColor: '#3e1a1a',
                textColor: '#ff0000',
                duration: 5000,
                effects: {
                    animation: 'sirenFlash',
                    particle: 'chaos',
                    redAlert: true,
                    soundTrigger: 'alarm.wav'
                }
            });
        }
    },
    triggerSwear: function(newCount) {
        if (window.asylumEffects) {
            window.asylumEffects.triggerEffect({
                textPrompt: `SWEAR JAR: ${newCount}`,
                backgroundColor: '#1a1a3e',
                textColor: '#00ffff',
                duration: 4000,
                effects: {
                    animation: 'electricPulse',
                    svgMask: 'glassDistortion',
                    particle: 'sparks',
                    screenFlicker: true,
                    soundTrigger: 'electroshock.wav'
                }
            });
        }
    }
};
