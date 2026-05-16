import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Card } from '../features/game/game.models';
import {
  CardSet,
  CardSetManifest,
  PLACEHOLDER_MANIFEST,
  PLACEHOLDER_SET_ID,
  buildCardSet,
} from './card-set.models';

const CARD_SETS_ENDPOINT = '/api/v1/card-sets';

@Injectable({ providedIn: 'root' })
export class CardSetService {
  private readonly http = inject(HttpClient);

  private readonly manifestsSig = signal<readonly CardSetManifest[]>([PLACEHOLDER_MANIFEST]);
  private readonly activeSetIdSig = signal<string>(PLACEHOLDER_SET_ID);

  // Per (setId, card) pair we've already warned about, so a missing asset
  // doesn't spam the console.
  private readonly warned = new Set<string>();

  private readonly placeholder = buildCardSet(PLACEHOLDER_MANIFEST);

  readonly manifests = computed(() => this.manifestsSig());
  readonly activeSetId = computed(() => this.activeSetIdSig());
  readonly activeSet = computed<CardSet>(() => {
    const id = this.activeSetIdSig();
    const manifest = this.manifestsSig().find((m) => m.id === id);
    return manifest ? buildCardSet(manifest) : this.placeholder;
  });

  setActiveSet(id: string): void {
    this.activeSetIdSig.set(id);
  }

  /**
   * Fetch the installed card-set manifests from the backend. Safe to call
   * multiple times — later calls overwrite the cached list. Failures keep
   * the placeholder-only default so the UI never breaks.
   */
  async loadManifests(): Promise<void> {
    try {
      const list = await firstValueFrom(this.http.get<CardSetManifest[]>(CARD_SETS_ENDPOINT));
      const next = list.length === 0 ? [PLACEHOLDER_MANIFEST] : list;
      // Always ensure the placeholder entry exists so resolveFallback works.
      const withPlaceholder = next.some((m) => m.id === PLACEHOLDER_SET_ID)
        ? next
        : [...next, PLACEHOLDER_MANIFEST];
      this.manifestsSig.set(withPlaceholder);
    } catch {
      // Keep the seeded placeholder; UI continues to render schematics.
    }
  }

  /** Resolves the URL to use when the active set's asset 404s. */
  fallbackFrontUrl(card: Card, missingSetId: string): string {
    const key = `${missingSetId}:${card.suit}:${card.rank}`;
    if (!this.warned.has(key)) {
      this.warned.add(key);
      console.warn(
        `[card-sets] missing asset for ${card.suit}/${card.rank} in set "${missingSetId}"; falling back to placeholder`,
      );
    }
    return this.placeholder.resolveFront(card);
  }

  fallbackBackUrl(missingSetId: string): string {
    const key = `${missingSetId}:back`;
    if (!this.warned.has(key)) {
      this.warned.add(key);
      console.warn(
        `[card-sets] missing back asset in set "${missingSetId}"; falling back to placeholder`,
      );
    }
    return this.placeholder.resolveBack();
  }
}
