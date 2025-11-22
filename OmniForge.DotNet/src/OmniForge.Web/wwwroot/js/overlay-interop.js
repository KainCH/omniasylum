window.overlayInterop = {
    init: function() {
        if (window.asylumEffects) {
            console.log("AsylumEffects initialized via Interop");
        } else {
            console.error("AsylumEffects not found!");
        }
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
