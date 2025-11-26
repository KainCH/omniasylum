window.overlayInterop = {
    init: () => {
        if (typeof AsylumEffects !== 'undefined') {
            window.asylumEffects = new AsylumEffects();
            console.log('AsylumEffects initialized');
        } else {
            console.error('AsylumEffects class not found');
        }
    },
    triggerAlert: (type, data) => {
        if (window.asylumEffects) {
            console.log(`Triggering alert: ${type}`, data);
            window.asylumEffects.trigger(type, data);
        }
    },
    playNotificationSound: (eventType) => {
        // If there is a separate audio manager
    }
};
