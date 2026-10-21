{ Linked list with pointers: ordered insertion, search, deletion with
  dispose, reversal in place. }
program lists(output);
type link = ^node;
     node = record
               key: integer;
               next: link
            end;
var head: link;
    i: integer;

procedure insert(var list: link; k: integer);
var p, q, n: link;
begin
   new(n); n^.key := k;
   p := list; q := nil;
   while p <> nil do
      if p^.key < k then begin q := p; p := p^.next end
      else p := nil;
   if q = nil then begin n^.next := list; list := n end
   else begin n^.next := q^.next; q^.next := n end
end;

procedure delete(var list: link; k: integer);
var p, q: link;
    found: boolean;
begin
   p := list; q := nil; found := false;
   while (p <> nil) and not found do
      if p^.key = k then found := true
      else begin q := p; p := p^.next end;
   if found then
   begin
      if q = nil then list := p^.next else q^.next := p^.next;
      dispose(p)
   end
end;

procedure reverse(var list: link);
var p, q, r: link;
begin
   p := list; q := nil;
   while p <> nil do
   begin
      r := p^.next; p^.next := q; q := p; p := r
   end;
   list := q
end;

procedure print(list: link);
begin
   while list <> nil do
   begin
      write(list^.key:4);
      list := list^.next
   end;
   writeln
end;

begin
   head := nil;
   for i := 1 to 10 do insert(head, (i * 37) mod 23);
   write('sorted:  '); print(head);
   delete(head, 14); delete(head, 1); delete(head, 99);
   write('deleted: '); print(head);
   reverse(head);
   write('reverse: '); print(head)
end.
