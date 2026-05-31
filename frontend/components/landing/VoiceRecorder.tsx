import React, { useState, useRef } from 'react';
import { Mic, Square, Loader2 } from 'lucide-react';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';

interface VoiceRecorderProps {
  onTranscription: (text: string) => void;
  disabled?: boolean;
}

export default function VoiceRecorder({ onTranscription, disabled }: VoiceRecorderProps) {
  const [isRecording, setIsRecording] = useState(false);
  const [isTranscribing, setIsTranscribing] = useState(false);
  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const showToast = useToastStore(state => state.showToast);

  const startRecording = async () => {
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const mediaRecorder = new MediaRecorder(stream);
      mediaRecorderRef.current = mediaRecorder;
      chunksRef.current = [];

      mediaRecorder.ondataavailable = (e) => {
        if (e.data.size > 0) chunksRef.current.push(e.data);
      };

      mediaRecorder.onstop = async () => {
        const audioBlob = new Blob(chunksRef.current, { type: 'audio/webm' });
        await handleTranscription(audioBlob);
        stream.getTracks().forEach(track => track.stop());
      };

      mediaRecorder.start();
      setIsRecording(true);
    } catch (error) {
      console.error('Error accessing microphone:', error);
      showToast('Microphone access was denied or an error occurred.', 'error');
    }
  };

  const stopRecording = () => {
    if (mediaRecorderRef.current && isRecording) {
      mediaRecorderRef.current.stop();
      setIsRecording(false);
    }
  };

  const handleTranscription = async (blob: Blob) => {
    setIsTranscribing(true);
    try {
      const file = new File([blob], "recording.webm", { type: "audio/webm" });
      const text = await schemaService.transcribeVoice(file);
      onTranscription(text);
    } catch (error) {
      console.error('Transcription failed:', error);
      showToast('An error occurred while transcribing audio.', 'error');
    } finally {
      setIsTranscribing(false);
    }
  };

  if (isTranscribing) {
    return (
      <button disabled className="p-3 bg-zinc-800 rounded-full text-zinc-400">
        <Loader2 className="w-5 h-5 animate-spin" />
      </button>
    );
  }

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={isRecording ? stopRecording : startRecording}
      className={`p-3 rounded-full transition-all flex items-center justify-center shadow-lg ${
        isRecording 
          ? 'bg-red-500/20 text-red-500 hover:bg-red-500/30 border border-red-500/50 animate-pulse' 
          : 'bg-zinc-800 text-zinc-400 hover:text-white hover:bg-zinc-700 border border-zinc-700'
      } disabled:opacity-50 disabled:cursor-not-allowed`}
      title={isRecording ? 'Stop Recording' : 'Voice Input'}
    >
      {isRecording ? <Square className="w-5 h-5" /> : <Mic className="w-5 h-5" />}
    </button>
  );
}
