import { Card } from '../features/game/game.models';

export interface CardSetManifest {
  id: string;
  name: string;
  license: string;
  preview: string;
  fileExtension: string;
  filePattern: string;
  back: string;
}

export interface CardSet {
  id: string;
  name: string;
  resolveFront(card: Card): string;
  resolveBack(): string;
}

export const PLACEHOLDER_SET_ID = 'placeholder';

export const PLACEHOLDER_MANIFEST: CardSetManifest = {
  id: PLACEHOLDER_SET_ID,
  name: 'Placeholder',
  license: 'Bundled — schematic',
  preview: 'preview.svg',
  fileExtension: 'svg',
  filePattern: '{suit}-{rank}.{ext}',
  back: 'back.svg',
};

const SUIT_LOWER: Record<Card['suit'], string> = {
  Bastoni: 'bastoni',
  Coppe: 'coppe',
  Denari: 'denari',
  Spade: 'spade',
};

const RANK_LOWER: Record<Card['rank'], string> = {
  Asso: 'asso',
  Tre: 'tre',
  Re: 're',
  Cavallo: 'cavallo',
  Fante: 'fante',
  Sette: 'sette',
  Sei: 'sei',
  Cinque: 'cinque',
  Quattro: 'quattro',
  Due: 'due',
};

export function buildCardSet(manifest: CardSetManifest): CardSet {
  const base = `/card-sets/${manifest.id}`;
  return {
    id: manifest.id,
    name: manifest.name,
    resolveFront(card) {
      const file = manifest.filePattern
        .replace('{suit}', SUIT_LOWER[card.suit])
        .replace('{rank}', RANK_LOWER[card.rank])
        .replace('{ext}', manifest.fileExtension);
      return `${base}/${file}`;
    },
    resolveBack() {
      return `${base}/${manifest.back}`;
    },
  };
}
