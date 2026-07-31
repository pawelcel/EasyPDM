import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"

function TagFilterSelect({
  tags,
  value,
  onChange,
}: {
  tags: string[]
  value: string
  onChange: (tag: string) => void
}) {
  return (
    <Select
      value={value || "all"}
      onValueChange={(v) => onChange(v === "all" ? "" : (v as string))}
    >
      <SelectTrigger className="min-w-36">
        <SelectValue>{(v: string) => (v === "all" || !v ? "Wszystkie tagi" : v)}</SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="all">Wszystkie tagi</SelectItem>
        {tags.map((t) => (
          <SelectItem key={t} value={t}>
            {t}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}

export { TagFilterSelect }
