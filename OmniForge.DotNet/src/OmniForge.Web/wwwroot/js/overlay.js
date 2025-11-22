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
            // Map legacy event types if needed, or ensure data matches what AsylumEffects expects
            console.log(`Triggering alert: ${type}`, data);
            // The legacy code calls: new AsylumEffects().trigger(type, data)
            // But AsylumEffects seems to be designed to be instantiated once?
            // The legacy code: const socket = io(); ... socket.on(..., () => new AsylumEffects().trigger(...))
            // It instantiates it every time? Let's check the class definition again.
            // "constructor() { this.activeEffects = []; ... }"
            // If it maintains state (activeEffects), it should be a singleton.
            // But if legacy code creates new instance every time, maybe it doesn't matter or it's stateless enough?
            // "this.activeEffects = []" suggests it tracks effects.
            // If I create a new one, I lose track of previous ones.
            // Let's stick to a singleton.
            window.asylumEffects.trigger(type, data);
        }
    },
    playNotificationSound: (eventType) => {
        // If there is a separate audio manager
    }
};
