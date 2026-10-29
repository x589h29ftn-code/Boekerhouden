// Start de factuurviewer in het externe venster (de pagina zelf mag geen inline script hebben).
import { extern } from './factuurviewer.js';

const container = document.getElementById('factuurviewer');
if (container) {
    extern(container);
}
