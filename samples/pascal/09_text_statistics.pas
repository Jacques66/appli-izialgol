{ Text processing with eoln and eof: reads the input file character by
  character and counts lines, words and letters (letter frequencies with a
  set of char and an array indexed by char). }
program textstat(input, output);
var ch: char;
    lines, words, chars: integer;
    inword: boolean;
    letters: set of char;
    freq: array['a'..'z'] of integer;
    c: char;

function lower(c: char): char;
begin
   if c in ['A'..'Z'] then lower := chr(ord(c) - ord('A') + ord('a'))
   else lower := c
end;

begin
   letters := ['a'..'z', 'A'..'Z'];
   for c := 'a' to 'z' do freq[c] := 0;
   lines := 0; words := 0; chars := 0;
   while not eof do
   begin
      inword := false;
      while not eoln do
      begin
         read(ch);
         chars := chars + 1;
         if ch in letters then
         begin
            freq[lower(ch)] := freq[lower(ch)] + 1;
            if not inword then begin words := words + 1; inword := true end
         end
         else inword := false
      end;
      readln;
      lines := lines + 1
   end;
   writeln('lines: ', lines:1, ', words: ', words:1, ', characters: ', chars:1);
   writeln('letter frequencies:');
   for c := 'a' to 'z' do
      if freq[c] > 0 then writeln('  ', c, freq[c]:4)
end.
