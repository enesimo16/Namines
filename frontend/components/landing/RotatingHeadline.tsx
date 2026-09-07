'use client';

import { useEffect, useState } from 'react';

const WORDS = [
  'modern SaaS',
  'e-commerce',
  'fintech apps',
  'AI agent memory',
  'multi-tenant',
  'PostgreSQL',
];

export default function RotatingHeadline() {
  const [wordIndex, setWordIndex] = useState(0);
  const [text, setText] = useState('');
  const [isDeleting, setIsDeleting] = useState(false);

  useEffect(() => {
    const currentWord = WORDS[wordIndex];
    let timer: NodeJS.Timeout;

    if (!isDeleting && text === currentWord) {
      // Pause when full word is typed
      timer = setTimeout(() => setIsDeleting(true), 1800);
    } else if (isDeleting && text === '') {
      // Move to next word after deleting
      setIsDeleting(false);
      setWordIndex((prev) => (prev + 1) % WORDS.length);
    } else {
      // Typing or backspacing speed
      const speed = isDeleting ? 35 : 75;
      timer = setTimeout(() => {
        setText((prev) =>
          isDeleting
            ? currentWord.substring(0, prev.length - 1)
            : currentWord.substring(0, prev.length + 1)
        );
      }, speed);
    }

    return () => clearTimeout(timer);
  }, [text, isDeleting, wordIndex]);

  return (
    <span
      className="inline-flex items-center whitespace-nowrap font-extrabold select-none"
      style={{ color: 'var(--namines-teal-vibrant)' }}
      aria-label="a variety of real-world database architectures"
    >
      <span>{text}</span>
      <span
        className="inline-block w-[3px] h-[0.9em] ml-1 bg-current animate-pulse align-middle"
        aria-hidden="true"
      />
    </span>
  );
}
