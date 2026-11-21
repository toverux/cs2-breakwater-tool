import type { ModRegistrar } from 'cs2/modding';
import { preloadImages } from 'cs2/utils';
import { register as registerBreakwaterTool } from './breakwater-tool';
import editorIconSrc from './icons/breakwater-tool-editor.svg';
import landscapingIconSrc from './icons/breakwater-tool-landscaping.svg';
import { iconsole } from './iconsole';
import { logError } from './utils';

// The game's preloader loads an image with its UI and holds it for the session, as it does for its
// own glyphs: a `ui-mods` image first fetched as it shows tries each UI mod's folder in turn, at
// least a frame each, and blinks in late.
// The editor toolbar's icon and the Landscaping menu's are named by the C# side alone, as
// `coui://ui-mods/images/breakwater-tool-editor.svg` and `…/breakwater-tool-landscaping.svg`: the
// imports are what makes the build deploy them.
preloadImages([editorIconSrc, landscapingIconSrc]);

/**
 * The features registering on the game's UI, each on its own: one that throws is reported and
 * skipped, and the others still register.
 */
const registrars: readonly ModRegistrar[] = [registerBreakwaterTool];

const register: ModRegistrar = moduleRegistry => {
  // The game calls every mod's registrar in one loop with no try/catch, so a throw here would also
  // keep every mod after this one from registering.
  let failed = false;

  for (const registrar of registrars) {
    try {
      registrar(moduleRegistry);
    } catch (error) {
      failed = true;

      logError(error, true);
    }
  }

  if (!failed) {
    iconsole.info(`BreakwaterTool: Registered all modules.`);
  }
};

// noinspection JSUnusedGlobalSymbols
export const hasCSS = true;

// oxlint-disable-next-line import/no-default-export - the game mod loader requires a default export
export default register;
