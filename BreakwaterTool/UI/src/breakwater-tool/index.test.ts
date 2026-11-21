import { describe, expect, it } from 'bun:test';
import { getModule, type ModuleRegistry } from 'cs2/modding';
import { register } from '.';
import { readGameBundle } from '../testing/game-setup';

interface Targets {
  readonly appended: string[];

  readonly extended: Array<readonly [module: string, exportName: string]>;
}

/**
 * What the registrar names in the game's module registry: the slots it appends to and the module
 * exports it extends.
 */
function recordTargets(): Targets {
  const targets: Targets = { appended: [], extended: [] };

  const registry: Pick<ModuleRegistry, 'append' | 'extend'> = {
    append: (target: string) => {
      targets.appended.push(target);
    },
    extend: (module: string, exportName: unknown) => {
      targets.extended.push([module, String(exportName)]);
    }
  };

  // A recording stand-in: the game hands its registry to mods' registrars alone.
  register(registry as unknown as ModuleRegistry);

  return targets;
}

// On a failure, the game moved or renamed what the registrar names since an update: find it in the
// readable bundle, then update the registrar (the bwt-game-update skill).
describe(`the tool's registrar`, () => {
  it(`extends module exports the game's bundle holds`, () => {
    const { extended } = recordTargets();

    const missing = extended.filter(([module, exportName]) => {
      try {
        // The registry throws for a module or an export it does not hold.
        getModule(module, exportName);

        return false;
      } catch {
        return true;
      }
    });

    expect(extended.length).toBeGreaterThan(0);
    expect(missing).toEqual([]);
  });

  it(`appends to slots the game's bundle renders`, () => {
    const bundle = readGameBundle();
    const { appended } = recordTargets();

    // A slot is a `ModdingHook` the game renders by name, which no module export shows.
    const missing = appended.filter(slot => !bundle.includes(`{name:"${slot}"}`));

    expect(appended.length).toBeGreaterThan(0);
    expect(missing).toEqual([]);
  });
});
