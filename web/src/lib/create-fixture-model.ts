import { createFixtureService } from './session-client.ts';
import { createShellModel } from './shell-model.ts';
import { initialLocale } from './localization.ts';

export function createFixtureModel() {
  const fixture = createFixtureService();
  const model = createShellModel(() => fixture.connect(), initialLocale());
  const unsubscribe = model.subscribe(state => fixture.setBehavior(state.behavior));
  const dispose = () => { unsubscribe(); model.dispose(); };
  return { model, dispose };
}
