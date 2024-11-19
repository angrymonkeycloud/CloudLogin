
window.deleteElement = function (selector) {
    const element = document.querySelector(selector);

    if (element)
        element.parentNode.removeChild(element);
}