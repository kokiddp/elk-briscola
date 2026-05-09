import { render } from '@testing-library/angular';
import { test } from 'vitest';
import { App } from './app';

test('renders without crashing', async () => {
  await render(App);
});
