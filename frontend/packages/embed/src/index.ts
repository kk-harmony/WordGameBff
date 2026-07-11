import { mount, registerWordGameElement, version } from './word-game-widget.js';

registerWordGameElement();

window.WordGame = {
  mount,
  version,
};

export { WordGameElement, mount, version } from './word-game-widget.js';
export { validateApiBase } from './validate.js';
