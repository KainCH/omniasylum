// Admin debug page interop functions
// Safe JS functions to avoid using eval()

/**
 * Focuses an element by its ID.
 * @param {string} elementId - The ID of the element to focus
 */
window.focusElement = function(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.focus();
    }
};
