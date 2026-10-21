program charsets(input, output);
var vowels, consonants, letters, seen: set of char; c: char; nv, nc: integer;
begin
   letters := ['a'..'z'];
   vowels := ['a', 'e', 'i', 'o', 'u', 'y'];
   consonants := letters - vowels;
   seen := [];
   nv := 0; nc := 0;
   while not eof do
   begin
      while not eoln do
      begin
         read(c);
         if c in vowels then nv := nv + 1
         else if c in consonants then nc := nc + 1;
         seen := seen + [c]
      end;
      readln
   end;
   writeln(nv:1, ' vowels, ', nc:1, ' consonants');
   for c := 'a' to 'z' do if c in seen * letters then write(c);
   writeln
end.
