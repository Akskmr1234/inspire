import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import type { ApiError } from '@/lib/api';
import { PHOTO_SLOTS, shrinkPicture } from '@/lib/photos';

/**
 * Up to three photographs of a record, each saved the moment it is chosen.
 *
 * Saved straight away rather than with the record's own form: a picture is not
 * something to lose because somebody closed the form, and the first slot is the one
 * a card or a list shows.
 */
export function PhotoSlots({
  queryKey,
  load,
  save,
  canEdit,
}: {
  readonly queryKey: readonly unknown[];
  readonly load: () => Promise<readonly (string | null)[]>;
  readonly save: (slot: number, content: string | null) => Promise<void>;
  readonly canEdit: boolean;
}): React.JSX.Element {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [problem, setProblem] = useState<string | null>(null);

  const photos = useQuery<readonly (string | null)[], ApiError>({
    queryKey,
    queryFn: load,
  });

  const store = useMutation<void, ApiError, { slot: number; content: string | null }>({
    mutationFn: ({ slot, content }) => save(slot, content),
    onSuccess: () => queryClient.invalidateQueries({ queryKey }),
    onError: (failure) => setProblem(failure.detail || failure.code),
  });

  return (
    <div className="space-y-2">
      {problem && <p className="alert-error text-xs">{problem}</p>}
      {photos.isError && (
        <p className="alert-error text-xs">{photos.error.detail || photos.error.code}</p>
      )}
      <div className="grid max-w-3xl grid-cols-1 gap-3 sm:grid-cols-3">
        {Array.from({ length: PHOTO_SLOTS }, (_, index) => (
          <PhotoSlot
            key={index}
            number={index + 1}
            content={photos.data?.[index] ?? null}
            loading={photos.isPending}
            busy={store.isPending}
            canEdit={canEdit}
            onPick={(file) => {
              setProblem(null);
              shrinkPicture(file)
                .then((content) => store.mutate({ slot: index + 1, content }))
                .catch(() => setProblem(t('photos.invalid')));
            }}
            onRemove={() => store.mutate({ slot: index + 1, content: null })}
          />
        ))}
      </div>
    </div>
  );
}

function PhotoSlot({
  number,
  content,
  loading,
  busy,
  canEdit,
  onPick,
  onRemove,
}: {
  readonly number: number;
  readonly content: string | null;
  readonly loading: boolean;
  readonly busy: boolean;
  readonly canEdit: boolean;
  readonly onPick: (file: File) => void;
  readonly onRemove: () => void;
}): React.JSX.Element {
  const { t } = useTranslation();
  const input = useRef<HTMLInputElement>(null);

  return (
    <div className="space-y-1.5">
      <div className="grid aspect-square place-items-center overflow-hidden rounded-xl border border-line bg-surface-2">
        {content ? (
          <img
            src={content}
            alt={t('photos.photo', { number })}
            className="size-full object-contain"
          />
        ) : (
          <span className="text-xs text-ink-subtle">
            {loading ? t('common.loading') : t('photos.photo', { number })}
          </span>
        )}
      </div>
      {canEdit && (
        <div className="flex gap-1.5">
          <input
            ref={input}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(event) => {
              const file = event.target.files?.[0];
              event.target.value = '';
              if (file) onPick(file);
            }}
          />
          <button
            type="button"
            disabled={busy}
            onClick={() => input.current?.click()}
            className="btn-secondary btn-sm flex-1"
          >
            {content ? t('photos.change') : t('photos.add')}
          </button>
          {content && (
            <button
              type="button"
              disabled={busy}
              onClick={onRemove}
              className="btn-secondary btn-sm text-red-600"
            >
              {t('photos.remove')}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
